using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// Account configuration. Balance uses DECIMAL(19,4) — 15 integer digits + 4 fractional,
/// sufficient for all ISO currencies including zero-decimal ones represented with 4 places.
/// Unique index on (MerchantId, AccountType, Currency) ensures one account per type per currency.
/// Restrict delete — accounts and their ledger history are never deleted with the merchant.
/// </summary>
public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.MerchantId)
            .IsRequired();

        builder.Property(a => a.AccountType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(a => a.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(a => a.Balance)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .IsRequired();

        builder.Property(a => a.RowVersion)
            .IsRowVersion()
            .IsRequired();

        builder.HasIndex(a => new { a.MerchantId, a.AccountType, a.Currency })
            .IsUnique();

        builder.HasIndex(a => a.MerchantId);

        // Restrict delete: financial history must never be cascade-deleted.
        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(a => a.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
