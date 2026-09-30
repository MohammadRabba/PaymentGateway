using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Merchants;
using PaymentGateway.Application.Options;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.ValueObjects;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Redis;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace PaymentGateway.IntegrationTests.Fixtures;

/// <summary>
/// Shared fixture for integration tests. Spins up real SQL Server, Redis, and RabbitMQ containers
/// via Testcontainers. Used as an IClassFixture by test classes. Provides a configured DbContext,
/// a Redis connection, and a pre-registered test merchant for tests to use.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
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

    private ServiceProvider? _services;
    private IServiceScope? _scope;

    public string SqlConnectionString => _sqlServer.GetConnectionString();
    public string RedisConnectionString => $"{_redis.Hostname}:{_redis.GetMappedPublicPort(6379)},abortConnect=false";
    public string RabbitMqConnectionString => $"amqp://guest:guest@{_rabbitMq.Hostname}:{_rabbitMq.GetMappedPublicPort(5672)}/%2f";

    public IPaymentGatewayDbContext DbContext => _scope!.ServiceProvider.GetRequiredService<IPaymentGatewayDbContext>();
    public IClock Clock => _scope!.ServiceProvider.GetRequiredService<IClock>();
    public IIdGenerator IdGenerator => _scope!.ServiceProvider.GetRequiredService<IIdGenerator>();
    public IIdempotencyStore IdempotencyStore => _scope!.ServiceProvider.GetRequiredService<IIdempotencyStore>();

    public TestMerchantInfo TestMerchant { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _sqlServer.StartAsync(),
            _redis.StartAsync(),
            _rabbitMq.StartAsync());

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();
        _scope = _services.CreateScope();

        // Apply migrations to the real SQL Server.
        var dbContext = _scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();
        await dbContext.Database.MigrateAsync();

        // Register a test merchant for tests to use.
        await CreateTestMerchantAsync();
    }

    public async Task DisposeAsync()
    {
        _scope?.Dispose();
        _services?.Dispose();
        await Task.WhenAll(
            _sqlServer.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbitMq.DisposeAsync().AsTask());
    }

    public IServiceScope CreateScope() => _services!.CreateScope();

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<PaymentGatewayDbContext>(options =>
            options.UseSqlServer(SqlConnectionString));
        services.AddScoped<IPaymentGatewayDbContext>(sp => sp.GetRequiredService<PaymentGatewayDbContext>());

        services.AddSingleton<IClock, PaymentGateway.Infrastructure.Time.SystemClock>();
        services.AddSingleton<IIdGenerator, PaymentGateway.Infrastructure.Time.SequentialGuidGenerator>();
        services.AddSingleton<ICorrelationContext, PaymentGateway.Infrastructure.Time.CorrelationContext>();
        services.Configure<IdempotencyOptions>(_ => { });

        services.AddSingleton(new RedisOptions { ConnectionString = RedisConnectionString });
        services.AddSingleton<RedisConnection>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
    }

    private async Task CreateTestMerchantAsync()
    {
        var merchantService = _scope!.ServiceProvider.GetRequiredService<MerchantService>();
        var request = new PaymentGateway.Application.Contracts.RegisterMerchantRequest
        {
            ExternalReference = "test-merchant-001",
            Name = "Test Merchant",
            WebhookUrl = "https://example.com/webhook",
            IdempotencyKey = "init-merchant-001",
        };
        var response = await merchantService.RegisterAsync(request, CancellationToken.None);

        TestMerchant = new TestMerchantInfo(
            response.Id,
            response.ApiKey ?? throw new InvalidOperationException("API key was not returned from registration."),
            response.WebhookUrl);
    }

    public record TestMerchantInfo(Guid Id, string ApiKey, string WebhookUrl);
}

/// <summary>
/// Marker for IClassFixture usage. xUnit creates one instance per test class, shared across all
/// tests in the class. The IntegrationFixture's containers are started once and torn down once.
/// </summary>
[CollectionDefinition("Integration")]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture> { }
