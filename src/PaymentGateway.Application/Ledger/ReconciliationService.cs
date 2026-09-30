using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Auditing;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Application.Ledger;

/// <summary>
/// Verifies that Account.Balance (materialised) equals SUM(LedgerEntries) (authoritative) for every
/// account. Discrepancies are REPORTED, never auto-repaired. Auto-repairing would hide bugs.
/// The reconciliation result is also emitted as a metric and an audit record.
/// </summary>
public sealed class ReconciliationService
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly IClock _clock;
    private readonly AuditService _auditService;

    public ReconciliationService(
        IPaymentGatewayDbContext dbContext,
        IClock clock,
        AuditService auditService)
    {
        _dbContext = dbContext;
        _clock = clock;
        _auditService = auditService;
    }

    /// <summary>
    /// Reconcile all accounts. Returns a report of any discrepancies found.
    /// </summary>
    public async Task<ReconciliationResponse> ReconcileAllAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var accounts = await _dbContext.Accounts
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var discrepancies = new List<AccountDiscrepancy>();

        foreach (var account in accounts)
        {
            var calculatedBalance = await CalculateLedgerBalanceAsync(account.Id, cancellationToken);
            if (calculatedBalance != account.Balance)
            {
                discrepancies.Add(new AccountDiscrepancy
                {
                    AccountId = account.Id,
                    MerchantId = account.MerchantId,
                    AccountType = account.AccountType.ToString(),
                    Currency = account.Currency,
                    StoredBalance = account.Balance,
                    CalculatedBalance = calculatedBalance,
                    Difference = account.Balance - calculatedBalance,
                });
            }
        }

        if (discrepancies.Count > 0)
        {
            _auditService.Record(
                "system",
                "ReconciliationDiscrepancy",
                "Reconciliation",
                Guid.Empty,
                new { AccountsChecked = accounts.Count, DiscrepanciesFound = discrepancies.Count });
        }

        return new ReconciliationResponse
        {
            ReconciledAt = now,
            AccountsChecked = accounts.Count,
            DiscrepanciesFound = discrepancies.Count,
            Discrepancies = discrepancies,
        };
    }

    /// <summary>
    /// Reconcile a single account. Returns the discrepancy or null if the account is consistent.
    /// </summary>
    public async Task<AccountDiscrepancy?> ReconcileAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null)
        {
            return null;
        }

        var calculatedBalance = await CalculateLedgerBalanceAsync(accountId, cancellationToken);
        if (calculatedBalance == account.Balance)
        {
            return null;
        }

        return new AccountDiscrepancy
        {
            AccountId = account.Id,
            MerchantId = account.MerchantId,
            AccountType = account.AccountType.ToString(),
            Currency = account.Currency,
            StoredBalance = account.Balance,
            CalculatedBalance = calculatedBalance,
            Difference = account.Balance - calculatedBalance,
        };
    }

    /// <summary>
    /// Calculate the authoritative balance for an account by summing all ledger entries with the
    /// account's natural direction. For asset accounts (AcquirerReceivable), debit increases and
    /// credit decreases. For liability/revenue accounts (MerchantPayable, FeeRevenue), the opposite.
    /// </summary>
    private async Task<decimal> CalculateLedgerBalanceAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var entries = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(e => e.AccountId == accountId)
            .ToListAsync(cancellationToken);

        var account = await _dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null)
        {
            return 0m;
        }

        // Replicate the DirectionMultiplier logic from Account.ApplyDebit/ApplyCredit.
        // Asset accounts: debit +, credit -. Liability/revenue accounts: debit -, credit +.
        var balance = 0m;
        foreach (var entry in entries)
        {
            var multiplier = (account.AccountType, entry.EntryType) switch
            {
                (AccountType.MerchantPayable, EntryType.Credit) => +1,
                (AccountType.MerchantPayable, EntryType.Debit) => -1,
                (AccountType.AcquirerReceivable, EntryType.Debit) => +1,
                (AccountType.AcquirerReceivable, EntryType.Credit) => -1,
                (AccountType.FeeRevenue, EntryType.Credit) => +1,
                (AccountType.FeeRevenue, EntryType.Debit) => -1,
                _ => throw new InvalidOperationException($"Unknown account type / entry type combination."),
            };
            balance += multiplier * entry.Amount;
        }

        return balance;
    }
}
