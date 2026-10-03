using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Persistence;

/// <summary>
/// Persistence abstraction for the Application layer. Implemented by PaymentGatewayDbContext
/// in Infrastructure. This interface breaks the circular dependency: Application needs to
/// access the DB but must not reference Infrastructure. SQL Server is the authoritative ledger.
/// </summary>
public interface IPaymentGatewayDbContext
{
    DbSet<Merchant> Merchants { get; }

    DbSet<Account> Accounts { get; }

    DbSet<Payment> Payments { get; }

    DbSet<Refund> Refunds { get; }

    DbSet<LedgerTransaction> LedgerTransactions { get; }

    DbSet<LedgerEntry> LedgerEntries { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<OutboxDeadLetter> OutboxDeadLetters { get; }

    DbSet<WebhookDelivery> WebhookDeliveries { get; }

    DbSet<AuditRecord> AuditRecords { get; }

    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    DbSet<RiskAssessment> RiskAssessments { get; }

    /// <summary>
    /// EF Core Database facade. Used for raw SQL operations where SQL Server-specific
    /// locking hints (UPDLOCK, HOLDLOCK) are required for the narrow serializable critical section.
    /// </summary>
    DatabaseFacade Database { get; }

    /// <summary>
    /// EF Core change tracker for managing entity state.
    /// </summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default);
}
