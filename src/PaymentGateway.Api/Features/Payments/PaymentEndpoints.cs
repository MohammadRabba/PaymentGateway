using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Api.Infrastructure.Authentication;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Payments;

namespace PaymentGateway.Api.Features.Payments;

/// <summary>
/// Payment endpoints. All mutating endpoints require X-Idempotency-Key header.
///
/// POST /api/v1/payments — Create and authorize a payment (Pending → Processing → {Authorized, Failed, Unknown}).
/// GET  /api/v1/payments/{id} — Get payment status.
/// POST /api/v1/payments/{id}/settle — Settle an authorized payment (posts the ledger).
/// POST /api/v1/payments/{id}/refunds — Create a refund.
/// GET  /api/v1/payments/{id}/ledger — Get ledger entries for a payment.
/// </summary>
public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/payments").WithTags("Payments");

        group.MapPost("/", CreatePayment);
        group.MapGet("/{paymentId:guid}", GetPayment);
        group.MapPost("/{paymentId:guid}/settle", SettlePayment);
        group.MapPost("/{paymentId:guid}/refunds", CreateRefund);
        group.MapGet("/{paymentId:guid}/ledger", GetPaymentLedger);

        return app;
    }

    private static async Task<IResult> CreatePayment(
        [FromBody] CreatePaymentApiRequest body,
        HttpContext httpContext,
        MerchantContext merchantContext,
        CreatePaymentHandler handler,
        CancellationToken cancellationToken)
    {
        var merchantId = RequireMerchant(merchantContext);
        if (merchantId is null)
        {
            return Results.Unauthorized();
        }

        var idempotencyKey = RequireIdempotencyKey(httpContext);
        if (idempotencyKey is null)
        {
            return IdempotencyKeyMissingResponse(httpContext);
        }

        var request = new CreatePaymentRequest
        {
            MerchantId = merchantId.Value,
            Amount = body.Amount,
            Currency = body.Currency,
            CardToken = body.CardToken,
            IdempotencyKey = idempotencyKey,
            Description = body.Description,
        };

        var response = await handler.HandleAsync(request, cancellationToken);
        return Results.Created($"/api/v1/payments/{response.PaymentId}", response);
    }

    private static async Task<IResult> GetPayment(
        Guid paymentId,
        MerchantContext merchantContext,
        GetPaymentHandler handler,
        CancellationToken cancellationToken)
    {
        var merchantId = RequireMerchant(merchantContext);
        if (merchantId is null)
        {
            return Results.Unauthorized();
        }

        var response = await handler.HandleAsync(paymentId, merchantId.Value, cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> SettlePayment(
        Guid paymentId,
        HttpContext httpContext,
        MerchantContext merchantContext,
        SettlePaymentHandler handler,
        CancellationToken cancellationToken)
    {
        var merchantId = RequireMerchant(merchantContext);
        if (merchantId is null)
        {
            return Results.Unauthorized();
        }

        var idempotencyKey = RequireIdempotencyKey(httpContext);
        if (idempotencyKey is null)
        {
            return IdempotencyKeyMissingResponse(httpContext);
        }

        var request = new SettlePaymentRequest
        {
            PaymentId = paymentId,
            MerchantId = merchantId.Value,
            IdempotencyKey = idempotencyKey,
        };

        var response = await handler.HandleAsync(request, cancellationToken);
        return Results.Ok(response);
    }

    private static async Task<IResult> CreateRefund(
        Guid paymentId,
        [FromBody] RefundApiRequest body,
        HttpContext httpContext,
        MerchantContext merchantContext,
        PaymentGateway.Application.Refunds.RefundPaymentHandler handler,
        CancellationToken cancellationToken)
    {
        var merchantId = RequireMerchant(merchantContext);
        if (merchantId is null)
        {
            return Results.Unauthorized();
        }

        var idempotencyKey = RequireIdempotencyKey(httpContext);
        if (idempotencyKey is null)
        {
            return IdempotencyKeyMissingResponse(httpContext);
        }

        var request = new RefundRequest
        {
            PaymentId = paymentId,
            MerchantId = merchantId.Value,
            IdempotencyKey = idempotencyKey,
            Amount = body.Amount,
            Currency = body.Currency,
            Reason = body.Reason,
        };

        var response = await handler.HandleAsync(request, cancellationToken);
        return Results.Created($"/api/v1/payments/{paymentId}/refunds/{response.RefundId}", response);
    }

    private static async Task<IResult> GetPaymentLedger(
        Guid paymentId,
        MerchantContext merchantContext,
        PaymentGateway.Application.Ledger.LedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        var merchantId = RequireMerchant(merchantContext);
        if (merchantId is null)
        {
            return Results.Unauthorized();
        }

        var entries = await ledgerService.GetEntriesForPaymentAsync(paymentId, merchantId.Value, cancellationToken);
        return Results.Ok(entries);
    }

    private static Guid? RequireMerchant(MerchantContext context)
    {
        if (!context.IsAuthenticated || context.IsAdmin)
        {
            return null;
        }

        return context.MerchantId;
    }

    private static string? RequireIdempotencyKey(HttpContext httpContext)
    {
        var key = httpContext.Request.Headers["X-Idempotency-Key"].ToString();
        return string.IsNullOrEmpty(key) ? null : key;
    }

    private static IResult IdempotencyKeyMissingResponse(HttpContext httpContext)
    {
        return Results.Problem(
            statusCode: 400,
            type: "https://payment-gateway.example.com/problems/missing-idempotency-key",
            title: "Missing idempotency key",
            detail: "The X-Idempotency-Key header is required for this operation.",
            instance: httpContext.Request.Path.Value);
    }
}
