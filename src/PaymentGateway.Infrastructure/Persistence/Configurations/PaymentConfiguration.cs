using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// Payment configuration. IdempotencyKey uniqueness is scoped per Merchant via unique index
/// (MerchantId, IdempotencyKey) — the SQL-side correctness boundary for payment idempotency.
/// All monetary fields use DECIMAL(19,4). rowversion protects the Status/TotalRefunded against
/// concurrent modifications.
/// </summary>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.MerchantId)
            .IsRequired();

        builder.Property(p => p.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(p => p.Operation)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.Amount)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(p => p.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.AuthCode)
            .HasMaxLength(64);

        builder.Property(p => p.AcquirerReference)
            .HasMaxLength(128);

        builder.Property(p => p.TotalRefunded)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(p => p.HeartbeatAt);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.AuthorizedAt);
        builder.Property(p => p.SettledAt);
        builder.Property(p => p.FailedAt);

        builder.Property(p => p.FailureReason)
            .HasMaxLength(500);

        builder.Property(p => p.RowVersion)
            .IsRowVersion()
            .IsRequired();

        // Uniqueness scoped by merchant + idempotency key. Idempotency correctness boundary.
        builder.HasIndex(p => new { p.MerchantId, p.IdempotencyKey })
            .IsUnique();

        builder.HasIndex(p => p.MerchantId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.AcquirerReference);

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(p => p.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
