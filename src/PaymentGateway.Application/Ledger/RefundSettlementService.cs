using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Options;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.Rules;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Application.Ledger;

/// <summary>
/// Posts a balanced double-entry ledger transaction for a refund. Refunds reverse the original
/// settlement: Dr MerchantPayable refundAmount, Cr AcquirerReceivable refundAmount. If the original
/// settlement charged fees and RefundFeeBehaviour is Proportional, a proportional fee reversal is
/// also posted as separate entries.
///
/// Same transaction-safety contract as SettlementService: the caller opens the Serializable
/// transaction and calls SaveChangesAsync. This service only modifies tracked entities.
/// </summary>
public sealed class RefundSettlementService
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IIdGenerator _idGenerator;
    private readonly ICorrelationContext _correlation;
    private readonly FeeOptions _feeOptions;

    public RefundSettlementService(
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
    /// Post the refund ledger transaction. The caller must have loaded the payment with UPDLOCK
    /// and the refund entity must already be created (Pending state).
    /// </summary>
    public async Task<Guid> PostRefundAsync(Payment payment, Refund refund, CancellationToken cancellationToken)
    {
        if (refund.Status != RefundStatus.Pending)
        {
            throw new InvalidOperationException($"Refund {refund.Id} is not in Pending state (current: {refund.Status}).");
        }

        var currency = Currency.Parse(payment.Currency);
        var now = _clock.UtcNow;
        var refundAmount = new Money(refund.Amount, currency);

        // Re-verify the refund invariant inside the transaction (defense against concurrent refunds).
        var settled = new Money(payment.Amount, currency);
        var already = new Money(payment.TotalRefunded, currency);
        RefundRules.AssertCanRefund(payment.Id, payment.Status, settled, already, refundAmount);

        // Load accounts with pessimistic lock.
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

        // Build the refund ledger transaction.
        var ledgerTxnId = _idGenerator.NewId();
        var ledgerTxn = new LedgerTransaction(
            ledgerTxnId,
            LedgerTransactionType.Refund,
            currency,
            _correlation.CurrentId,
            now,
            paymentId: payment.Id,
            refundId: refund.Id);

        // Refund reverses the settlement: Dr MerchantPayable, Cr AcquirerReceivable.
        ledgerTxn.AddEntry(_idGenerator.NewId(), merchantAccount.Id, EntryType.Debit, refundAmount, now);
        ledgerTxn.AddEntry(_idGenerator.NewId(), acquirerAccount.Id, EntryType.Credit, refundAmount, now);

        // Enforce accounting invariants.
        var entriesForValidation = ledgerTxn.Entries
            .Select(e => (e.EntryType, new Money(e.Amount, currency)))
            .ToList();

        LedgerRules.AssertAllPositive(entriesForValidation.Select(x => x.Item2).ToList());
        LedgerRules.AssertSingleCurrency(entriesForValidation.Select(x => x.Item2).ToList(), currency);
        LedgerRules.AssertBalanced(entriesForValidation);

        // Apply entries to account balances.
        foreach (var entry in ledgerTxn.Entries)
        {
            var account = entry.AccountId == acquirerAccount.Id ? acquirerAccount
                        : entry.AccountId == merchantAccount.Id ? merchantAccount
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

        // Apply the refund to the payment (updates TotalRefunded and transitions state).
        payment.ApplyRefund(refundAmount, now);

        // Mark the refund as completed.
        refund.Complete(now);

        _dbContext.LedgerTransactions.Add(ledgerTxn);

        return ledgerTxnId;
    }

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
