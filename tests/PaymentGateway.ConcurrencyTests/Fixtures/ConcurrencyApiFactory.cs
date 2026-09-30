using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Infrastructure.Persistence;
using Testcontainers.MsSql;
using Testcontainers.Redis;
using Testcontainers.RabbitMq;

namespace PaymentGateway.ConcurrencyTests.Fixtures;

/// <summary>
/// WebApplicationFactory-based fixture for concurrency tests. Spins up real containers and
/// configures the API to use them. Tests can issue parallel HTTP requests to verify invariants
/// hold under concurrent load.
/// </summary>
public sealed class ConcurrencyApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Your_password123")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3.13-management-alpine")
        .Build();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PaymentGateway"] = _sqlServer.GetConnectionString(),
                ["Redis:ConnectionString"] = $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)},abortConnect=false",
                ["RabbitMq:ConnectionUri"] = $"amqp://guest:guest@{_rabbitMq.Hostname}:{_rabbitMq.GetMappedPublicPort(5672)}/%2f",
                ["Security:RequireAdminKey"] = "false",
                ["Workers:StartupDelay"] = "00:00:01",
                ["Acquirer:Latency"] = "00:00:00.001",
            });
        });
    }

    async Task IAsyncLifetime.InitializeAsync()
    {
        await Task.WhenAll(
            _sqlServer.StartAsync(),
            _redis.StartAsync(),
            _rabbitMq.StartAsync());

        // Apply migrations to the test SQL Server.
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(
            _sqlServer.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbitMq.DisposeAsync().AsTask());
    }

    public IPaymentGatewayDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPaymentGatewayDbContext>();
    }
}

[CollectionDefinition("Concurrency")]
public sealed class ConcurrencyCollection : ICollectionFixture<ConcurrencyApiFactory> { }
