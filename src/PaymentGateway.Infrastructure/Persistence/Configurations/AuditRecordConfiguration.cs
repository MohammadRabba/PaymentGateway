using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// AuditRecord configuration. Append-only — no Update or Delete path in business code.
/// Metadata is JSON; never contains secrets (caller responsibility to sanitise).
/// </summary>
public sealed class AuditRecordConfiguration : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> builder)
    {
        builder.ToTable("AuditRecords");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.Actor)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(a => a.Action)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.AggregateType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.AggregateId)
            .IsRequired();

        builder.Property(a => a.CorrelationId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.OccurredAt)
            .IsRequired();

        builder.Property(a => a.Metadata)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.HasIndex(a => new { a.AggregateType, a.AggregateId });
        builder.HasIndex(a => a.CorrelationId);
        builder.HasIndex(a => a.OccurredAt);
    }
}
