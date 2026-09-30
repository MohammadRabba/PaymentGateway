using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// IdempotencyRecord configuration. UNIQUE(MerchantId, Operation, Key) is the SQL-side correctness
/// boundary for idempotency. Redis is the fast coordination cache; if it is unavailable, this
/// unique constraint still prevents duplicate financial execution.
/// </summary>
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.MerchantId)
            .IsRequired();

        builder.Property(r => r.Operation)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.Key)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(r => r.RequestHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(r => r.State)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.StatusCode);

        builder.Property(r => r.ResponsePayload)
            .HasColumnType("nvarchar(max)");

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.CompletedAt);

        builder.Property(r => r.ExpiresAt)
            .IsRequired();

        builder.HasIndex(r => new { r.MerchantId, r.Operation, r.Key })
            .IsUnique();

        builder.HasIndex(r => new { r.State, r.ExpiresAt });
    }
}
