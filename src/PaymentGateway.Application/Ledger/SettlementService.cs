using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Options;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Exceptions;
using PaymentGateway.Domain.Rules;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Application.Ledger;

/// <summary>
/// Posts a balanced double-entry ledger transaction for a payment settlement. This is the core
/// financial operation: Dr AcquirerReceivable gross, Cr MerchantPayable net, Cr FeeRevenue fee
/// (if fees are enabled). Account.Balance and Payment.Status are updated atomically in the SAME
/// transaction as the ledger entries.
///
/// IMPORTANT: This service does NOT call SaveChangesAsync or open its own transaction. The caller
/// (the handler) opens a Serializable transaction and calls SaveChangesAsync after invoking this
/// service. This ensures the ledger posting, account update, payment state transition, outbox
/// message, and audit record all commit atomically.
///
/// Idempotency: if the payment is already Settled (or partially refunded), the service returns
/// Guid.Empty without posting a duplicate ledger transaction. This is defense-in-depth even when
/// the idempotency check at the handler level has already passed (e.g., the idempotency record
/// expired but the payment was already settled).
/// </summary>
public sealed class SettlementService
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly ICorrelationContext _correlation;
    private readonly FeeOptions _feeOptions;

    public SettlementService(
        IPaymentGatewayDbContext dbContext,
        IClock clock,
        IIdGenerator idGenerator,
        ICorrelationContext correlation,
        IOptions<FeeOptions> feeOptions)
    {
        _dbContext = dbContext;
        _clock = clock;
        _idGenerator = idGenerator;
        _correlation = correlation;
        _feeOptions = feeOptions.Value;
    }

    /// <summary>
    /// Post the settlement ledger transaction for the given payment. The caller must have already
    /// loaded the payment with an UPDLOCK, HOLDLOCK hint (via FromSqlInterpolated) inside the
    /// Serializable transaction.
    /// </summary>
    /// <returns>The ledger transaction ID, or Guid.Empty if the payment was already settled.</returns>
    public async Task<Guid> PostSettlementAsync(Payment payment, CancellationToken cancellationToken)
    {
        // Idempotent: already settled (or partially refunded). No duplicate posting.
        if (payment.Status is PaymentStatus.Settled
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return Guid.Empty;
        }

        // The state machine enforces Authorized → Settled. Any other state throws.
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new InvalidPaymentStateException(payment.Status, PaymentStatus.Settled);
        }

        var currency = Currency.Parse(payment.Currency);
        var now = _clock.UtcNow;

        // Load accounts with pessimistic lock (UPDLOCK, HOLDLOCK) for the critical section.
        var acquirerAccount = await LoadAccountForUpdateAsync(
            payment.MerchantId, AccountType.AcquirerReceivable, currency, cancellationToken);
        var merchantAccount = await LoadAccountForUpdateAsync(
            payment.MerchantId, AccountType.MerchantPayable, currency, cancellationToken);

        if (acquirerAccount is null)
        {
            throw new AccountNotConfiguredException(payment.MerchantId, AccountType.AcquirerReceivable, currency.Code);
        }
        if (merchantAccount is null)
        {
            throw new AccountNotConfiguredException(payment.MerchantId, AccountType.MerchantPayable, currency.Code);
        }

        // Calculate fees.
        var gross = new Money(payment.Amount, currency);
        var fee = _feeOptions.CalculateFeesEnabled && _feeOptions.FeePercent > 0m
            ? CalculateFee(gross, currency)
            : Money.Zero(currency);
        var net = gross.Subtract(fee);

        // Build the ledger transaction and its entries.
        var ledgerTxnId = _idGenerator.NewId();
        var ledgerTxn = new LedgerTransaction(
            ledgerTxnId,
            LedgerTransactionType.PaymentSettlement,
            currency,
            _correlation.CurrentId,
            now,
            paymentId: payment.Id);

        // Dr AcquirerReceivable gross
        ledgerTxn.AddEntry(_idGenerator.NewId(), acquirerAccount.Id, EntryType.Debit, gross, now);
        // Cr MerchantPayable net
        ledgerTxn.AddEntry(_idGenerator.NewId(), merchantAccount.Id, EntryType.Credit, net, now);

        Account? feeAccount = null;
        if (!fee.IsZero)
        {
            feeAccount = await LoadAccountForUpdateAsync(
                payment.MerchantId, AccountType.FeeRevenue, currency, cancellationToken);
            if (feeAccount is null)
            {
                throw new AccountNotConfiguredException(payment.MerchantId, AccountType.FeeRevenue, currency.Code);
            }
            // Cr FeeRevenue fee
            ledgerTxn.AddEntry(_idGenerator.NewId(), feeAccount.Id, EntryType.Credit, fee, now);
        }

        // Enforce accounting invariants before applying to accounts.
        var entriesForValidation = ledgerTxn.Entries
            .Select(e => (e.EntryType, new Money(e.Amount, currency)))
            .ToList();

        LedgerRules.AssertAllPositive(entriesForValidation.Select(x => x.Item2).ToList());
        LedgerRules.AssertSingleCurrency(entriesForValidation.Select(x => x.Item2).ToList(), currency);
        LedgerRules.AssertBalanced(entriesForValidation);

        // Apply entries to account balances. Each entry updates the corresponding account.
        foreach (var entry in ledgerTxn.Entries)
        {
            var account = entry.AccountId == acquirerAccount.Id ? acquirerAccount
                        : entry.AccountId == merchantAccount.Id ? merchantAccount
                        : entry.AccountId == feeAccount?.Id ? feeAccount
                        : throw new InvalidOperationException($"Ledger entry references unknown account {entry.AccountId}.");

            var entryMoney = new Money(entry.Amount, currency);
            if (entry.EntryType == EntryType.Debit)
            {
                account.ApplyDebit(entryMoney, now);
            }
            else
            {
                account.ApplyCredit(entryMoney, now);
            }
        }

        // Transition payment to Settled.
        payment.Settle(now);

        // Add the ledger transaction to the DbContext. The caller's SaveChangesAsync persists it.
        _dbContext.LedgerTransactions.Add(ledgerTxn);

        return ledgerTxnId;
    }

    /// <summary>
    /// Calculate the fee for a gross amount. Banker's rounding at the currency's minor-unit precision.
    /// The fee is: gross * FeePercent / 100, rounded to the currency's decimal places.
    /// </summary>
    private Money CalculateFee(Money gross, Currency currency)
    {
        var rawFee = gross.Amount * _feeOptions.FeePercent / 100m;
        var roundedFee = Math.Round(rawFee, currency.DecimalPlaces, MidpointRounding.ToEven);
        return new Money(roundedFee, currency);
    }

    /// <summary>
    /// Load an account with SQL Server UPDLOCK + HOLDLOCK hints for the serializable critical section.
    /// This prevents other transactions from reading or modifying the row until the current
    /// transaction commits, ensuring no double settlement or inconsistent balance reads.
    /// </summary>
    private async Task<Account?> LoadAccountForUpdateAsync(
        Guid merchantId,
        AccountType accountType,
        Currency currency,
        CancellationToken cancellationToken)
    {
        var accountTypeCode = (int)accountType;
        return await _dbContext.Accounts
            .FromSqlInterpolated($"SELECT * FROM Accounts WITH (UPDLOCK, HOLDLOCK) WHERE MerchantId = {merchantId} AND AccountType = {accountTypeCode} AND Currency = {currency.Code}")
            .FirstOrDefaultAsync(cancellationToken);
    }
}
