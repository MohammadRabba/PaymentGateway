using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// LedgerEntry configuration. The Entries collection on LedgerTransaction is configured via
/// a backing field navigation. Append-only by convention — no Update/Delete path exists in business code.
/// </summary>
public sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.LedgerTransactionId)
            .IsRequired();

        builder.Property(e => e.AccountId)
            .IsRequired();

        builder.Property(e => e.EntryType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(e => e.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.HasIndex(e => e.LedgerTransactionId);
        builder.HasIndex(e => e.AccountId);

        builder.HasOne<LedgerTransaction>()
            .WithMany()
            .HasForeignKey(e => e.LedgerTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
