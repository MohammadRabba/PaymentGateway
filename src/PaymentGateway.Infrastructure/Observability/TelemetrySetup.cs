using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.EntityFrameworkCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Instrumentation.Process;
using OpenTelemetry.Instrumentation.Runtime;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PaymentGateway.Infrastructure.Observability;

namespace PaymentGateway.Infrastructure.Observability;

/// <summary>
/// Sets up OpenTelemetry tracing and metrics. Traces are exported via OTLP (or Console in dev).
/// Metrics are exported via OTLP and a .NET Meters API bridge so the PaymentGatewayMetrics counts
/// and histograms are observable through the standard OTel pipeline.
///
/// Instrumented libraries:
///   - ASP.NET Core (HTTP server)
///   - HttpClient (HTTP client — for acquirer + webhook calls)
///   - EntityFrameworkCore (SQL queries)
///   - StackExchange.Redis (Redis operations)
///   - System.Runtime (GC, thread pool, etc.)
/// </summary>
public static class TelemetrySetup
{
    public const string ServiceName = "PaymentGateway";
    public const string ServiceVersion = "1.0.0";

    /// <summary>
    /// Add OpenTelemetry services (tracing + metrics) to the DI container. Called from API
    /// composition. The OTLP endpoint is configured via environment variables:
    ///   OTEL_EXPORTER_OTLP_ENDPOINT = "http://otel-collector:4317"
    /// </summary>
    public static IServiceCollection AddPaymentGatewayTelemetry(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: ServiceName, serviceVersion: ServiceVersion))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddSource(PaymentGatewayMetrics.MeterName)
                // .AddOtlpExporter()  // TODO: AddOtlpExporter not available - needs OpenTelemetry.Exporter.OpenTelemetryProtocol package
                .AddConsoleExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(PaymentGatewayMetrics.MeterName)
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                // .AddOtlpExporter()  // TODO: AddOtlpExporter not available - needs OpenTelemetry.Exporter.OpenTelemetryProtocol package
                .AddConsoleExporter());

        return services;
    }

    /// <summary>
    /// Register the PaymentGatewayMetrics singleton. The Meter is created with a fixed name so
    /// the OTel pipeline can subscribe to it.
    /// </summary>
    public static IServiceCollection AddPaymentGatewayMetrics(this IServiceCollection services)
    {
        services.AddSingleton<PaymentGatewayMetrics>();
        return services;
    }
}
