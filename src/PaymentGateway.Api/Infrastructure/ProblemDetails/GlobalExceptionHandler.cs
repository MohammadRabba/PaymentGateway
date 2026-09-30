using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Domain.Exceptions;

namespace PaymentGateway.Api.Infrastructure.ProblemDetails;

/// <summary>
/// Global exception handler. Catches all unhandled exceptions and converts them to RFC 7807
/// ProblemDetails responses. Never leaks stack traces, SQL errors, connection strings, or
/// secrets. Domain exceptions map to their configured HTTP status; everything else maps to 500.
///
/// Registered via WebApplicationExtensions.UseGlobalExceptionHandler.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, type, title, detail) = MapException(exception);

        // Log only — never expose the message body to the client for 5xx errors.
        if (status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception: {ExceptionType}", exception.GetType().Name);
        }
        else
        {
            _logger.LogInformation("Domain exception: {ExceptionType} - {Message}", exception.GetType().Name, exception.Message);
        }

        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Type = type,
            Title = title,
            Status = status,
            Detail = status >= 500 ? "An internal error occurred." : detail,
            Instance = httpContext.Request.Path.Value,
        };

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        if (httpContext.Response.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
        {
            problem.Extensions["correlationId"] = correlationId.ToString();
        }

        // Add domain-specific extensions for richer error context.
        if (exception is DomainException dex)
        {
            foreach (var prop in dex.GetType().GetProperties())
            {
                if (prop.Name is nameof(DomainException.Message) or nameof(DomainException.ProblemTypeUri) or nameof(DomainException.HttpStatusCode))
                {
                    continue;
                }

                var value = prop.GetValue(dex);
                if (value is not null)
                {
                    problem.Extensions[prop.Name] = value;
                }
            }
        }

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }

    private static (int status, string type, string title, string detail) MapException(Exception exception) =>
        exception switch
        {
            PaymentGateway.Application.Exceptions.PaymentNotFoundException ex
                => (404, ex.ProblemTypeUri, "Payment not found", ex.Message),
            PaymentGateway.Application.Exceptions.MerchantNotFoundException ex
                => (404, ex.ProblemTypeUri, "Merchant not found", ex.Message),
            PaymentGateway.Application.Exceptions.AccountNotFoundException ex
                => (404, ex.ProblemTypeUri, "Account not found", ex.Message),
            PaymentGateway.Application.Exceptions.LedgerTransactionNotFoundException ex
                => (404, ex.ProblemTypeUri, "Ledger transaction not found", ex.Message),
            PaymentGateway.Application.Exceptions.RefundNotFoundException ex
                => (404, ex.ProblemTypeUri, "Refund not found", ex.Message),

            PaymentGateway.Application.Exceptions.MerchantIsolationException ex
                => (403, ex.ProblemTypeUri, "Forbidden", ex.Message),

            PaymentGateway.Application.Exceptions.IdempotencyInProgressException ex
                => (409, ex.ProblemTypeUri, "Request in progress", ex.Message),

            InvalidPaymentStateException ex
                => (ex.HttpStatusCode, ex.ProblemTypeUri, "Invalid payment state", ex.Message),
            PaymentGateway.Domain.Exceptions.InvalidRefundStateException ex
                => (ex.HttpStatusCode, ex.ProblemTypeUri, "Invalid refund state", ex.Message),
            ConcurrencyConflictException ex
                => (ex.HttpStatusCode, ex.ProblemTypeUri, "Concurrency conflict", ex.Message),
            UnknownAcquirerOutcomeException ex
                => (ex.HttpStatusCode, ex.ProblemTypeUri, "Unknown acquirer outcome", ex.Message),

            CurrencyMismatchException ex
                => (ex.HttpStatusCode, ex.ProblemTypeUri, "Currency mismatch", ex.Message),
            RefundExceedsSettledAmountException ex
                => (ex.HttpStatusCode, ex.ProblemTypeUri, "Refund exceeds settled amount", ex.Message),
            IdempotencyKeyReuseException ex
                => (ex.HttpStatusCode, ex.ProblemTypeUri, "Idempotency key reused", ex.Message),

            LedgerNotBalancedException ex
                => (ex.HttpStatusCode, ex.ProblemTypeUri, "Ledger not balanced", "An internal accounting error occurred."),
            PaymentGateway.Application.Exceptions.AccountNotConfiguredException ex
                => (ex.HttpStatusCode, ex.ProblemTypeUri, "Account not configured", "Internal misconfiguration."),

            ArgumentException ex
                => (400, "https://payment-gateway.example.com/problems/validation", "Validation error", ex.Message),
            InvalidOperationException ex
                => (400, "https://payment-gateway.example.com/problems/business-rule", "Business rule violation", ex.Message),

            _ => (500, "https://payment-gateway.example.com/problems/internal", "Internal error", "An internal error occurred."),
        };
}
