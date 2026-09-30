using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Persistence;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>
/// Database initializer. On startup, applies pending EF Core migrations and (optionally) seeds
/// a dev merchant so the API can be exercised locally without manual SQL.
///
/// MIGRATION STRATEGY:
///   - Development: this initializer applies migrations automatically on startup.
///   - Production: the migrations image (Dockerfile.migrations) runs `dotnet ef database update`
///     as a one-shot job before the API starts. The API's startup call to ApplyMigrationsAsync
///     is a no-op if there are no pending migrations, so it's safe to keep enabled.
///
/// Migration failures are NOT silently swallowed — they throw, which causes the API to fail to
/// start. This is intentional: a payment gateway MUST NOT run against an inconsistent schema.
/// </summary>
public sealed class DbInitializer
{
    private readonly IPaymentGatewayDbContext _dbContext;
    private readonly ILogger<DbInitializer> _logger;

    public DbInitializer(IPaymentGatewayDbContext dbContext, ILogger<DbInitializer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Applying database migrations...");

        try
        {
            // The DatabaseFacade is from Microsoft.EntityFrameworkCore.Infrastructure — agnostic
            // to the provider. MigrateAsync is the EF Core API that applies pending migrations.
            await _dbContext.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Database migrations applied.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed. The API will not start.");
            throw;
        }
    }
}
