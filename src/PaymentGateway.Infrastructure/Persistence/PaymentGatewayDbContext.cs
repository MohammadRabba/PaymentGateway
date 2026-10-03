using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for the payment gateway. SQL Server is the authoritative financial ledger.
/// All monetary fields use DECIMAL(19, 4). All mutable financial aggregates use rowversion for
/// optimistic concurrency. Implements IPaymentGatewayDbContext so the Application layer can depend
/// on an abstraction without referencing Infrastructure (avoids circular project dependency).
/// </summary>
public sealed class PaymentGatewayDbContext : DbContext, IPaymentGatewayDbContext
{
    public DbSet<Merchant> Merchants => Set<Merchant>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Refund> Refunds => Set<Refund>();

    public DbSet<LedgerTransaction> LedgerTransactions => Set<LedgerTransaction>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<OutboxDeadLetter> OutboxDeadLetters => Set<OutboxDeadLetter>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();

    public PaymentGatewayDbContext(DbContextOptions<PaymentGatewayDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Entity configurations are split per type for maintainability.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentGatewayDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    Task<IDbContextTransaction> IPaymentGatewayDbContext.BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    Task<IDbContextTransaction> IPaymentGatewayDbContext.BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(isolationLevel, cancellationToken);
}
