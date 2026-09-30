using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PaymentGateway.Api.Features.Accounts;
using PaymentGateway.Api.Features.Admin;
using PaymentGateway.Api.Features.Ledger;
using PaymentGateway.Api.Features.Merchants;
using PaymentGateway.Api.Features.Payments;
using PaymentGateway.Api.Infrastructure.ProblemDetails;
using PaymentGateway.Api.Middleware;

namespace PaymentGateway.Api.Extensions;

/// <summary>
/// Configures the HTTP pipeline. The order of middleware matters:
///   1. Correlation (sets X-Correlation-ID before anything else logs)
///   2. RequestLogging (logs every request with correlation)
///   3. ApiKeyAuth (authenticates the merchant; sets MerchantContext)
///   4. ExceptionHandler (catches all unhandled exceptions → ProblemDetails)
///   5. Endpoints (route handlers)
///   6. Health checks
/// </summary>
public static class WebApplicationExtensions
{
    public static WebApplication UsePaymentGatewayPipeline(this WebApplication app)
    {
        // Order is important.

        // Global exception handler — must come first so it catches everything downstream.
        app.UseExceptionHandler();

        // Correlation middleware — sets X-Correlation-ID so subsequent logs have it.
        app.UseMiddleware<CorrelationMiddleware>();

        // Request logging — logs method, path, status, duration.
        app.UseMiddleware<RequestLoggingMiddleware>();

        // API key authentication — sets MerchantContext.
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

        return app;
    }

    public static WebApplication MapPaymentGatewayEndpoints(this WebApplication app)
    {
        app.MapMerchantEndpoints();
        app.MapPaymentEndpoints();
        app.MapAccountEndpoints();
        app.MapLedgerEndpoints();
        app.MapAdminEndpoints();

        // Health checks — anonymous access (skip auth).
        app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = _ => false, // Liveness: no dependency checks. Just verify the process is alive.
        });

        app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"), // Readiness: verify SQL, Redis, RabbitMQ.
            ResponseWriter = HealthCheckResponseWriter.Write,
        });

        return app;
    }

    /// <summary>
    /// Custom health check response writer that produces a JSON body with per-check status.
    /// </summary>
    private static class HealthCheckResponseWriter
    {
        public static Task Write(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = report.Status == HealthStatus.Healthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;

            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    duration = e.Value.Duration.TotalMilliseconds,
                    description = e.Value.Description,
                }),
                totalDuration = report.TotalDuration.TotalMilliseconds,
            });

            return context.Response.WriteAsync(body);
        }
    }
}
