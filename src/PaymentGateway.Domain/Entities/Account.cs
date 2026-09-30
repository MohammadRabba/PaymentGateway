using PaymentGateway.Domain.Enums;
using PaymentGateway.Domain.ValueObjects;

namespace PaymentGateway.Domain.Entities;

/// <summary>
/// Aggregate root: Account. Balance is a materialised view of the ledger for fast reads;
/// the ledger is authoritative. Balance changes only happen inside the same SQL transaction
/// as the corresponding ledger entries. Mutations go through ApplyDebit / ApplyCredit.
/// </summary>
public sealed class Account
{
    public Guid Id { get; private set; }

    public Guid MerchantId { get; private set; }

    public AccountType AccountType { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    /// <summary>
    /// Materialised balance. Stored as DECIMAL(19,4) in SQL Server.
    /// </summary>
    public decimal Balance { get; private set; }

    public AccountStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    private Account() { }

    public Account(
        Guid id,
        Guid merchantId,
        AccountType accountType,
        Currency currency,
        DateTimeOffset createdAt)
    {
        Id = id;
        MerchantId = merchantId;
        AccountType = accountType;
        Currency = currency.Code;
        Balance = 0m;
        Status = AccountStatus.Active;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// Apply a debit. For asset accounts (AcquirerReceivable) debit increases balance;
    /// for liability/revenue accounts (MerchantPayable, FeeRevenue) debit decreases balance.
    /// This is the ONLY way to mutate balance; called inside the financial transaction only.
    /// </summary>
    public void ApplyDebit(Money amount, DateTimeOffset updatedAt)
    {
        AssertActive();
        AssertCurrency(amount);
        Balance += DirectionMultiplier(AccountType, EntryType.Debit) * amount.Amount;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Apply a credit. For asset accounts (AcquirerReceivable) credit decreases balance;
    /// for liability/revenue accounts (MerchantPayable, FeeRevenue) credit increases balance.
    /// </summary>
    public void ApplyCredit(Money amount, DateTimeOffset updatedAt)
    {
        AssertActive();
        AssertCurrency(amount);
        Balance += DirectionMultiplier(AccountType, EntryType.Credit) * amount.Amount;
        UpdatedAt = updatedAt;
    }

    public void Freeze(DateTimeOffset updatedAt)
    {
        Status = AccountStatus.Frozen;
        UpdatedAt = updatedAt;
    }

    public void Unfreeze(DateTimeOffset updatedAt)
    {
        Status = AccountStatus.Active;
        UpdatedAt = updatedAt;
    }

    private void AssertActive()
    {
        if (Status != AccountStatus.Active)
        {
            throw new InvalidOperationException($"Account {Id} is not active.");
        }
    }

    private void AssertCurrency(Money amount)
    {
        if (!string.Equals(Currency, amount.Currency.Code, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Account currency mismatch: account {Currency}, entry {amount.Currency.Code}.");
        }
    }

    /// <summary>
    /// +1 when the entry increases the natural balance, -1 when it decreases.
    /// AcquirerReceivable is asset-like: debit increases, credit decreases.
    /// MerchantPayable is liability-like: credit increases, debit decreases.
    /// FeeRevenue is revenue (credit-normal): credit increases, debit decreases.
    /// </summary>
    private static int DirectionMultiplier(AccountType type, EntryType entry) =>
        type switch
        {
            AccountType.MerchantPayable => entry == EntryType.Credit ? +1 : -1,
            AccountType.AcquirerReceivable => entry == EntryType.Debit ? +1 : -1,
            AccountType.FeeRevenue => entry == EntryType.Credit ? +1 : -1,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown account type"),
        };
}
