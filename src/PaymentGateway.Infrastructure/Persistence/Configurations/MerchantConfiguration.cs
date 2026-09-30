using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// Merchant configuration. ApiKeyHash is uniquely indexed for O(1) authentication lookups.
/// ExternalReference is uniquely indexed to prevent duplicate merchant onboarding.
/// Restrict delete behaviour — a merchant's financial history is never deleted.
/// </summary>
public sealed class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("Merchants");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.ExternalReference)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(m => m.WebhookUrl)
            .HasMaxLength(2048)
            .IsRequired();

        // Hex-encoded HMAC secret (64 chars for 32 bytes).
        builder.Property(m => m.WebhookSecret)
            .HasMaxLength(128)
            .IsRequired();

        // Hex-encoded SHA-256 hash (64 chars).
        builder.Property(m => m.ApiKeyHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(m => m.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired();

        builder.Property(m => m.UpdatedAt)
            .IsRequired();

        // SQL Server rowversion for optimistic concurrency.
        builder.Property(m => m.RowVersion)
            .IsRowVersion()
            .IsRequired();

        builder.HasIndex(m => m.ExternalReference)
            .IsUnique();

        builder.HasIndex(m => m.ApiKeyHash)
            .IsUnique();
    }
}
