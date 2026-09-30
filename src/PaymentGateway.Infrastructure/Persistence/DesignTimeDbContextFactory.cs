using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` works without running the full API host.
/// Connection string is overridden at runtime by Api composition via environment variable.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PaymentGatewayDbContext>
{
    public PaymentGatewayDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PaymentGateway")
            ?? "Server=localhost,1433;Database=PaymentGateway;User Id=sa;Password=Your_password123;TrustServerCertificate=True;MultipleActiveResultSets=true";

        var options = new DbContextOptionsBuilder<PaymentGatewayDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(PaymentGatewayDbContext).Assembly.FullName);
                sql.MigrationsHistoryTable("__EFMigrationsHistory");
            })
            .Options;

        return new PaymentGatewayDbContext(options);
    }
}
