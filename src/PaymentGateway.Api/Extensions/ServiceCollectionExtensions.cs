using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using PaymentGateway.Api.Infrastructure.Authentication;
using PaymentGateway.Application.Auditing;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Idempotency;
using PaymentGateway.Application.Ledger;
using PaymentGateway.Application.Merchants;
using PaymentGateway.Application.Options;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Application.Refunds;
using PaymentGateway.Application.Webhooks;
using PaymentGateway.Infrastructure.Messaging;
using PaymentGateway.Infrastructure.Messaging.Consumers;
using PaymentGateway.Infrastructure.Observability;
using PaymentGateway.Infrastructure.Payments;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Redis;
using PaymentGateway.Infrastructure.Resilience;
using PaymentGateway.Infrastructure.Security;
using PaymentGateway.Infrastructure.Time;
using PaymentGateway.Infrastructure.Webhooks;
using PaymentGateway.Infrastructure.Workers;

namespace PaymentGateway.Api.Extensions;

/// <summary>
/// DI composition. Registers every service with the correct lifetime. The key principle: Scoped
/// services (DbContext, handlers) are NEVER injected into singletons (workers) — workers use
/// IServiceScopeFactory instead. This is enforced by the worker constructors.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentGatewayServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Options ----
        services.Configure<FeeOptions>(configuration.GetSection(FeeOptions.SectionName));
        services.Configure<IdempotencyOptions>(configuration.GetSection(IdempotencyOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<AcquirerOptions>(configuration.GetSection(AcquirerOptions.SectionName));
        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.SectionName));
        services.Configure<ResilienceOptions>(configuration.GetSection(ResilienceOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<WebhookOptions>(configuration.GetSection(WebhookOptions.SectionName));
        services.Configure<ApiKeyAuthOptions>(configuration.GetSection(ApiKeyAuthOptions.SectionName));
        // ---- Fraud options & services ----
        services.Configure<FraudOptions>(configuration.GetSection(FraudOptions.SectionName));

        // Register IFraudDetectionClient using IHttpClientFactory. The HttpClient base address and
        // timeout are configured from FraudOptions at runtime.
        services.AddHttpClient<PaymentGateway.Application.Common.IFraudDetectionClient, PaymentGateway.Infrastructure.Fraud.HttpFraudDetectionClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentGateway.Application.Options.FraudOptions>>().Value;
            if (!string.IsNullOrEmpty(opts.ServiceUrl))
            {
                client.BaseAddress = new Uri(opts.ServiceUrl);
            }
            client.Timeout = opts.Timeout;
        });

        // RiskGate orchestrator (scoped per request) — follows same lifetime as handlers and DbContext.
        services.AddScoped<PaymentGateway.Application.Risk.RiskGate>();

        // ---- DbContext (Scoped) ----
        // The DbContext is the authoritative ledger. Scoped lifetime so each request gets its own
        // change tracker. Workers use IServiceScopeFactory to create a fresh scope per iteration.
        services.AddDbContext<PaymentGatewayDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("PaymentGateway"),
                sql =>
                {
                    sql.MigrationsAssembly(typeof(PaymentGatewayDbContext).Assembly.FullName);
                    sql.MigrationsHistoryTable("__EFMigrationsHistory");
                    sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                });
        });

        // Register IPaymentGatewayDbContext to resolve to the concrete DbContext.
        services.AddScoped<PaymentGateway.Application.Persistence.IPaymentGatewayDbContext>(sp =>
            sp.GetRequiredService<PaymentGatewayDbContext>());

        // ---- Singletons (stateless or thread-safe) ----
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, SequentialGuidGenerator>();
        services.AddSingleton<ICorrelationContext, CorrelationContext>();
        services.AddSingleton<RequestFingerprinter>();
        services.AddSingleton<WebhookSigner>();
        services.AddSingleton<WebhookPayloadFactory>();
        services.AddSingleton<ResiliencePipelineFactory>();
        services.AddSingleton<PaymentGatewayMetrics>();

        // ---- Scoped (per-request or per-worker-iteration) ----
        services.AddScoped<MerchantContext>(); // Concrete scoped context for middleware.
        services.AddScoped<IMerchantContext>(sp => sp.GetRequiredService<MerchantContext>());
        services.AddScoped<AuditService>();
        services.AddScoped<MerchantService>();
        services.AddScoped<LedgerService>();
        services.AddScoped<SettlementService>();
        services.AddScoped<RefundSettlementService>();
        services.AddScoped<ReconciliationService>();
        services.AddScoped<IdempotencyService>();
        services.AddScoped<CreatePaymentHandler>();
        services.AddScoped<GetPaymentHandler>();
        services.AddScoped<SettlePaymentHandler>();
        services.AddScoped<RecoverPaymentHandler>();
        services.AddScoped<PaymentGateway.Application.Refunds.RefundPaymentHandler>();

        // ---- Infrastructure singletons ----
        // RedisConnection is IDisposable (singleton) — disposed on app shutdown.
        services.AddSingleton<RedisConnection>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();

        // RabbitMQ connection + topology.
        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqTopology>();
        services.AddSingleton<RabbitMqEventPublisher>();

        // IAcquirerClient — the simulated acquirer. Singleton (thread-safe via ConcurrentDictionary).
        services.AddSingleton<PaymentGateway.Application.Common.IAcquirerClient, SimulatedAcquirerClient>();

        // Admin authenticator.
        services.AddSingleton<AdminAuthenticator>();

        // ---- Webhook HTTP client ----
        // Use IHttpClientFactory to avoid socket exhaustion.
        services.AddHttpClient<WebhookHttpClient>();

        // ---- Webhook delivery service (uses WebhookHttpClient) ----
        services.AddSingleton<WebhookDeliveryService>();

        // ---- Hosted workers ----
        services.AddHostedService<OutboxPublisherWorker>();
        services.AddHostedService<PendingPaymentRecoveryWorker>();
        services.AddHostedService<ReconciliationWorker>();
        services.AddHostedService<WebhookConsumerHostedService>();

        // ---- DbInitializer (used by Program.cs on startup) ----
        services.AddSingleton<DbInitializer>();

        // ---- Health checks ----
        var connString = configuration.GetConnectionString("PaymentGateway") ?? string.Empty;
        var redisConn = configuration.GetSection(RedisOptions.SectionName)["ConnectionString"] ?? string.Empty;
        var rabbitConn = configuration.GetSection(RabbitMqOptions.SectionName)["ConnectionUri"] ?? string.Empty;

        services.AddHealthChecks()
            .AddSqlServer(connString, name: "sql-server", tags: new[] { "ready" })
            .AddRedis(redisConn, name: "redis", tags: new[] { "ready" })
            .AddRabbitMQ(rabbitConn, name: "rabbitmq", tags: new[] { "ready" });

        // ---- OpenTelemetry ----
        services.AddPaymentGatewayTelemetry();
        services.AddPaymentGatewayMetrics();

        // ---- Swagger / OpenAPI ----
        services.AddPaymentGatewaySwagger();

        // ---- Global exception handler (IExceptionHandler) ----
        services.AddSingleton<PaymentGateway.Api.Infrastructure.ProblemDetails.GlobalExceptionHandler>();
        services.AddExceptionHandler<PaymentGateway.Api.Infrastructure.ProblemDetails.GlobalExceptionHandler>();

        return services;
    }

    public static IServiceCollection AddPaymentGatewaySwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Open Payment Gateway & Settlement Engine",
                Version = "v1",
                Description = "An enterprise payment gateway with double-entry ledger, transactional outbox, and reliable webhook delivery.",
            });

            // API key authentication header.
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "Authorization",
                Description = "Merchant API key. Format: 'Bearer pgk_...'",
            });

            options.AddSecurityDefinition("AdminKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Admin-Key",
                Description = "Admin API key for admin endpoints.",
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "ApiKey",
                    },
                }] = Array.Empty<string>(),
            });
        });

        return services;
    }
}

/// <summary>
/// Hosted service wrapper for WebhookEventConsumer. The consumer itself is a singleton (holds the
/// long-lived channel); this hosted service starts and stops it.
/// </summary>
public sealed class WebhookConsumerHostedService : IHostedService, IAsyncDisposable
{
    private readonly WebhookEventConsumer _consumer;
    private readonly ILogger<WebhookConsumerHostedService> _logger;

    public WebhookConsumerHostedService(
        WebhookEventConsumer consumer,
        ILogger<WebhookConsumerHostedService> logger)
    {
        _consumer = consumer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _consumer.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start WebhookEventConsumer. Webhooks will not be delivered until the consumer is restarted.");
            // Don't rethrow — the API should still start even if RabbitMQ is temporarily unavailable.
            // The consumer can be restarted manually or via a watchdog.
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _consumer.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping WebhookEventConsumer.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _consumer.DisposeAsync();
    }
}
