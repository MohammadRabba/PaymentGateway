using Microsoft.OpenApi.Models;
using PaymentGateway.Api.Extensions;
using PaymentGateway.Api.Infrastructure.ProblemDetails;
using PaymentGateway.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog: structured console output with correlation enricher.
builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    loggerConfig
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithCorrelationId()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {Message:lj}{NewLine}{Exception}");
});

// Explicitly set Kestrel limits (in addition to defaults) for production hardening.
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1024 * 1024; // 1 MB — payment requests are small.
    options.Limits.MaxConcurrentConnections = 256;
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
});

// Add API services + payment gateway services (including Swagger).
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddPaymentGatewayServices(builder.Configuration);

var app = builder.Build();

// Initialize database — apply migrations. Fail fast if migration fails (do not start against a
// broken schema).
try
{
    using var scope = app.Services.CreateScope();
    var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await initializer.InitializeAsync(app.Lifetime.ApplicationStopping);
}
catch (Exception ex)
{
    var bootstrapLogger = app.Services.GetRequiredService<ILogger<Program>>();
    bootstrapLogger.LogCritical(ex, "Database initialization failed. The API cannot start.");
    throw;
}

// Declare RabbitMQ topology (idempotent — safe to call on every startup).
try
{
    using var scope = app.Services.CreateScope();
    var topology = scope.ServiceProvider.GetRequiredService<PaymentGateway.Infrastructure.Messaging.RabbitMqTopology>();
    await topology.DeclareTopologyAsync(app.Lifetime.ApplicationStopping);
}
catch (Exception ex)
{
    var bootstrapLogger = app.Services.GetRequiredService<ILogger<Program>>();
    bootstrapLogger.LogError(ex, "RabbitMQ topology declaration failed. Webhooks will not be delivered until RabbitMQ is available.");
    // Don't rethrow — the API can still process payments; webhooks will queue in the outbox and
    // be delivered once RabbitMQ recovers and the worker retries.
}

// Configure the HTTP pipeline.
app.UsePaymentGatewayPipeline();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapPaymentGatewayEndpoints();

app.Run();

// Expose Program as a public partial class so the WebApplicationFactory<Program> pattern works
// for integration tests. This is the standard .NET 9 approach.
public partial class Program { }
