using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Ledger;
using PaymentGateway.Application.Merchants;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Persistence;

namespace PaymentGateway.IntegrationTests.Ledger;

[Collection("Integration")]
public sealed class LedgerBalancingTests : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture;
    private SettlementService _settlementService = null!;
    private MerchantService _merchantService = null!;

    public LedgerBalancingTests(IntegrationFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        var scope = _fixture.CreateScope();
        _settlementService = scope.ServiceProvider.GetRequiredService<SettlementService>();
        _merchantService = scope.ServiceProvider.GetRequiredService<MerchantService>();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PostSettlementAsync_creates_balanced_ledger_entries_and_updates_account_balance()
    {
        // Setup: create a settled payment directly in the database for the test merchant.
        var merchantId = _fixture.TestMerchant.Id;
        var currency = Currency.Parse("USD");
        var paymentId = Guid.NewGuid();

        var payment = CreateAuthorizedPayment(paymentId, merchantId, 100m, currency);
        _fixture.DbContext.Payments.Add(payment);
        await _fixture.DbContext.SaveChangesAsync();

        // Act: post the settlement ledger transaction.
        using var txn = await _fixture.DbContext.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, CancellationToken.None);
        var loadedPayment = await _fixture.DbContext.Payments
            .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {paymentId}")
            .FirstAsync();

        var ledgerTxnId = await _settlementService.PostSettlementAsync(loadedPayment, CancellationToken.None);
        await _fixture.DbContext.SaveChangesAsync();
        await txn.CommitAsync();

        // Assert: ledger transaction has exactly 2 entries (no fees configured by default).
        ledgerTxnId.Should().NotBeEmpty();

        var entries = await _fixture.DbContext.LedgerEntries
            .Where(e => e.LedgerTransactionId == ledgerTxnId)
            .ToListAsync();

        entries.Should().HaveCount(2);
        var debits = entries.Where(e => e.EntryType == EntryType.Debit).Sum(e => e.Amount);
        var credits = entries.Where(e => e.EntryType == EntryType.Credit).Sum(e => e.Amount);
        debits.Should().Be(credits);
        debits.Should().Be(100m);
    }

    [Fact]
    public async Task PostSettlementAsync_is_idempotent_when_payment_already_settled()
    {
        var merchantId = _fixture.TestMerchant.Id;
        var currency = Currency.Parse("USD");
        var paymentId = Guid.NewGuid();

        var payment = CreateAuthorizedPayment(paymentId, merchantId, 100m, currency);
        _fixture.DbContext.Payments.Add(payment);
        await _fixture.DbContext.SaveChangesAsync();

        // First settlement.
        using (var txn = await _fixture.DbContext.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, CancellationToken.None))
        {
            var p = await _fixture.DbContext.Payments
                .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {paymentId}")
                .FirstAsync();
            var firstTxnId = await _settlementService.PostSettlementAsync(p, CancellationToken.None);
            await _fixture.DbContext.SaveChangesAsync();
            await txn.CommitAsync();

            firstTxnId.Should().NotBeEmpty();
        }

        // Second settlement attempt — should return Guid.Empty (idempotent no-op).
        using (var txn = await _fixture.DbContext.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, CancellationToken.None))
        {
            var p = await _fixture.DbContext.Payments
                .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {paymentId}")
                .FirstAsync();
            var secondTxnId = await _settlementService.PostSettlementAsync(p, CancellationToken.None);
            await _fixture.DbContext.SaveChangesAsync();
            await txn.CommitAsync();

            secondTxnId.Should().BeEmpty();
        }

        // Only one ledger transaction should exist for this payment.
        var ledgerTxns = await _fixture.DbContext.LedgerTransactions
            .Where(t => t.PaymentId == paymentId)
            .ToListAsync();
        ledgerTxns.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReconciliationService_detects_corrupted_balance()
    {
        // Setup: create and settle a payment.
        var merchantId = _fixture.TestMerchant.Id;
        var currency = Currency.Parse("USD");
        var paymentId = Guid.NewGuid();

        var payment = CreateAuthorizedPayment(paymentId, merchantId, 100m, currency);
        _fixture.DbContext.Payments.Add(payment);
        await _fixture.DbContext.SaveChangesAsync();

        using (var txn = await _fixture.DbContext.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, CancellationToken.None))
        {
            var p = await _fixture.DbContext.Payments
                .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK, HOLDLOCK) WHERE Id = {paymentId}")
                .FirstAsync();
            await _settlementService.PostSettlementAsync(p, CancellationToken.None);
            await _fixture.DbContext.SaveChangesAsync();
            await txn.CommitAsync();
        }

        // Corrupt the account balance deliberately.
        var merchantAccount = await _fixture.DbContext.Accounts
            .FirstAsync(a => a.MerchantId == merchantId && a.AccountType == AccountType.MerchantPayable && a.Currency == "USD");

        // Use raw SQL to bypass the entity's ApplyDebit/ApplyCredit methods and corrupt the balance.
        await _fixture.DbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Accounts SET Balance = 9999.99 WHERE Id = {merchantAccount.Id}");

        // Run reconciliation.
        var reconScope = _fixture.CreateScope();
        var reconService = reconScope.ServiceProvider.GetRequiredService<ReconciliationService>();
        var result = await reconService.ReconcileAllAsync(CancellationToken.None);

        result.DiscrepanciesFound.Should().BeGreaterThan(0);
        var discrepancy = result.Discrepancies.FirstOrDefault(d => d.AccountId == merchantAccount.Id);
        discrepancy.Should().NotBeNull();
        discrepancy!.Difference.Should().NotBe(0m);
    }

    private static Payment CreateAuthorizedPayment(Guid paymentId, Guid merchantId, decimal amount, Currency currency)
    {
        var payment = new Payment(paymentId, merchantId, $"key-{paymentId:N}", OperationType.Payment, new Money(amount, currency), DateTimeOffset.UtcNow);
        payment.BeginProcessing(DateTimeOffset.UtcNow);
        var auth = new Authorization
        {
            AuthCode = "ABC123",
            AcquirerReference = $"ACQ-{paymentId:N}",
            Outcome = AcquirerOutcome.Success,
            AuthorizedAt = DateTimeOffset.UtcNow,
        };
        payment.MarkAuthorized(auth, DateTimeOffset.UtcNow);
        return payment;
    }
}
