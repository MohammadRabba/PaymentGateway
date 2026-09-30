using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PaymentGateway.Infrastructure.Persistence;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <summary>
/// Model snapshot for EF Core migration drift detection. Applies the same IEntityTypeConfiguration
/// instances that the runtime DbContext uses, producing an identical model. EF Core then diffs this
/// model against the live model when adding new migrations.
/// </summary>
[DbContext(typeof(PaymentGatewayDbContext))]
partial class PaymentGatewayDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "9.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 128);

        // Re-apply the same configurations the runtime DbContext applies. The resulting model
        // matches what EF would have inlined by hand, and supports accurate drift detection
        // when adding new migrations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentGatewayDbContext).Assembly);
#pragma warning restore 612, 618
    }
}
