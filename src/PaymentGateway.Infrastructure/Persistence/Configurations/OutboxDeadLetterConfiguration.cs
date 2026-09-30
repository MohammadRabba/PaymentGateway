using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// OutboxDeadLetter configuration. Mirror of OutboxMessage written when a message is poisoned.
/// Never deleted — operational audit trail for permanently failing financial events.
/// </summary>
public sealed class OutboxDeadLetterConfiguration : IEntityTypeConfiguration<OutboxDeadLetter>
{
    public void Configure(EntityTypeBuilder<OutboxDeadLetter> builder)
    {
        builder.ToTable("OutboxDeadLetters");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedNever();

        builder.Property(d => d.OutboxMessageId)
            .IsRequired();

        builder.Property(d => d.EventType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.AggregateId)
            .IsRequired();

        builder.Property(d => d.Payload)
            .HasColumnType("nvarchar(max)")
            .IsRequired();

        builder.Property(d => d.LastError)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(d => d.Attempts)
            .IsRequired();

        builder.Property(d => d.OriginalOccurredAt)
            .IsRequired();

        builder.Property(d => d.PoisonedAt)
            .IsRequired();

        builder.HasIndex(d => d.OutboxMessageId);
        builder.HasIndex(d => d.EventType);
    }
}
