using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Infrastructure.Persistence.Configurations;

public sealed class RiskAssessmentConfiguration : IEntityTypeConfiguration<RiskAssessment>
{
    public void Configure(EntityTypeBuilder<RiskAssessment> builder)
    {
        builder.ToTable("RiskAssessments");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.PaymentId).IsRequired();
        builder.Property(r => r.MerchantId).IsRequired();
        builder.Property(r => r.Score).HasPrecision(8, 6).IsRequired();
        builder.Property(r => r.Decision).HasConversion<int>().IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(r => r.ModelVersion).HasMaxLength(64).IsRequired();
        builder.Property(r => r.LatencyMs).IsRequired();
        builder.Property(r => r.AssessedAt).IsRequired();

        builder.HasIndex(r => r.PaymentId);
        builder.HasIndex(r => r.MerchantId);
        builder.HasIndex(r => r.Decision);
        builder.HasIndex(r => r.AssessedAt);

        // Restrict delete — risk history must never cascade-delete with a payment.
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
