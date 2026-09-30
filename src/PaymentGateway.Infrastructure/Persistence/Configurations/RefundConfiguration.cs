using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// Refund configuration. Has its own IdempotencyKey scoped by Merchant — refund keys never collide
/// with payment keys even if the merchant reuses the same client-generated value.
/// </summary>
public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("Refunds");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.PaymentId)
            .IsRequired();

        builder.Property(r => r.MerchantId)
            .IsRequired();

        builder.Property(r => r.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(r => r.Amount)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(r => r.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasMaxLength(500);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.CompletedAt);

        builder.Property(r => r.FailureReason)
            .HasMaxLength(500);

        builder.Property(r => r.RowVersion)
            .IsRowVersion()
            .IsRequired();

        builder.HasIndex(r => new { r.MerchantId, r.IdempotencyKey })
            .IsUnique();

        builder.HasIndex(r => r.PaymentId);
        builder.HasIndex(r => r.MerchantId);

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(r => r.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
