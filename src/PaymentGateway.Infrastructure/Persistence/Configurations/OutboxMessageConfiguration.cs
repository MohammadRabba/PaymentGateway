using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// OutboxMessage configuration. The (Status, NextAttemptAt) index supports the publisher's
/// claim query: SELECT messages WHERE Status=Pending AND NextAttemptAt &lt;= now. Poisoned messages
/// are never deleted — they remain in this table for inspection and are mirrored to OutboxDeadLetter.
/// </summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .ValueGeneratedNever();

        builder.Property(o => o.EventType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(o => o.AggregateId)
            .IsRequired();

        builder.Property(o => o.Payload)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(o => o.OccurredAt)
            .IsRequired();

        builder.Property(o => o.Attempts)
            .IsRequired();

        builder.Property(o => o.NextAttemptAt)
            .IsRequired();

        builder.Property(o => o.ProcessedAt);

        builder.Property(o => o.ClaimedBy)
            .HasMaxLength(64);

        builder.Property(o => o.ClaimedAt);

        builder.Property(o => o.LastError)
            .HasMaxLength(2000);

        builder.Property(o => o.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(o => new { o.Status, o.NextAttemptAt })
            .IncludeProperties(o => new { o.Id, o.EventType, o.AggregateId });

        builder.HasIndex(o => o.AggregateId);
    }
}
