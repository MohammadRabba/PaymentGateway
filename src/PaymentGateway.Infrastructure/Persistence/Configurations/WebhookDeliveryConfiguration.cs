using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

/// <summary>
/// WebhookDelivery configuration. EventId is uniquely indexed — consumer-side deduplication key.
/// If a delivery for an EventId already exists with status Delivered, the consumer ACKs the
/// RabbitMQ message without re-sending. This makes duplicate event delivery safe.
/// </summary>
public sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("WebhookDeliveries");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .ValueGeneratedNever();

        builder.Property(w => w.EventId)
            .IsRequired();

        builder.Property(w => w.WebhookId)
            .IsRequired();

        builder.Property(w => w.MerchantId)
            .IsRequired();

        builder.Property(w => w.EventType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(w => w.AttemptCount)
            .IsRequired();

        builder.Property(w => w.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(w => w.ResponseStatusCode);

        builder.Property(w => w.DeliveredAt);
        builder.Property(w => w.NextAttemptAt);

        builder.Property(w => w.LastError)
            .HasMaxLength(2000);

        builder.Property(w => w.PayloadHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(w => w.EventId)
            .IsUnique();

        builder.HasIndex(w => new { w.Status, w.NextAttemptAt });
        builder.HasIndex(w => w.MerchantId);
    }
}
