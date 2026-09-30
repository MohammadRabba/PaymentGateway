using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// LedgerTransaction configuration. Append-only: no Update/Delete path. PaymentId and RefundId
/// are optional because corrections are not tied to a specific payment/refund. The Entries
/// collection is exposed as IReadOnlyCollection on the entity and populated by EF via the backing field.
/// </summary>
public sealed class LedgerTransactionConfiguration : IEntityTypeConfiguration<LedgerTransaction>
{
    public void Configure(EntityTypeBuilder<LedgerTransaction> builder)
    {
        builder.ToTable("LedgerTransactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .ValueGeneratedNever();

        builder.Property(t => t.PaymentId);
        builder.Property(t => t.RefundId);

        builder.Property(t => t.Type)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(t => t.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(t => t.CorrelationId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        // Entries: navigation backed by private field _entries. EF populates the field directly.
        builder.HasMany(t => t.Entries)
            .WithOne()
            .HasForeignKey(e => e.LedgerTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(t => t.Entries)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => t.PaymentId);
        builder.HasIndex(t => t.RefundId);
        builder.HasIndex(t => t.CorrelationId);

        builder.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(t => t.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Refund>()
            .WithMany()
            .HasForeignKey(t => t.RefundId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
