using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Api.Infrastructure.Authentication;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Merchants;

namespace PaymentGateway.Api.Features.Merchants;

/// <summary>
/// Merchant endpoints. POST is admin-only (creates a new merchant + 3 default accounts per
/// currency, generates API key + webhook secret). GET returns merchant info (admin or self).
/// </summary>
public static class MerchantEndpoints
{
    public static IEndpointRouteBuilder MapMerchantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/merchants").WithTags("Merchants");

        group.MapPost("/", RegisterMerchant).WithMetadata(new EndpointNameMetadata("RegisterMerchant"));
        group.MapGet("/{merchantId:guid}", GetMerchant).WithMetadata(new EndpointNameMetadata("GetMerchant"));

        return app;
    }

    private static async Task<IResult> RegisterMerchant(
        [FromBody] RegisterMerchantApiRequest body,
        HttpContext httpContext,
        MerchantContext merchantContext,
        MerchantService merchantService,
        CancellationToken cancellationToken)
    {
        if (!merchantContext.IsAuthenticated || !merchantContext.IsAdmin)
        {
            return Results.Problem(
                statusCode: 403,
                type: "https://payment-gateway.example.com/problems/forbidden",
                title: "Forbidden",
                detail: "Admin API key required to register merchants.",
                instance: httpContext.Request.Path.Value);
        }

        // The IdempotencyKey is extracted from the X-Idempotency-Key header.
        var idempotencyKey = httpContext.Request.Headers["X-Idempotency-Key"].ToString();
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            return Results.Problem(
                statusCode: 400,
                type: "https://payment-gateway.example.com/problems/missing-idempotency-key",
                title: "Missing idempotency key",
                detail: "The X-Idempotency-Key header is required.",
                instance: httpContext.Request.Path.Value);
        }

        var request = new RegisterMerchantRequest
        {
            ExternalReference = body.ExternalReference,
            Name = body.Name,
            WebhookUrl = body.WebhookUrl,
            IdempotencyKey = idempotencyKey,
        };

        var response = await merchantService.RegisterAsync(request, cancellationToken);
        return Results.Created($"/api/v1/merchants/{response.Id}", response);
    }

    private static async Task<IResult> GetMerchant(
        Guid merchantId,
        HttpContext httpContext,
        MerchantContext merchantContext,
        MerchantService merchantService,
        CancellationToken cancellationToken)
    {
        if (!merchantContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        // Admin can view any merchant. A merchant can only view themselves.
        var effectiveId = merchantContext.IsAdmin
            ? merchantId
            : (merchantContext.MerchantId == merchantId ? merchantId : throw new MerchantIsolationException(merchantContext.MerchantId, merchantId));

        var response = await merchantService.GetAsync(effectiveId, cancellationToken);
        if (response is null)
        {
            return Results.Problem(
                statusCode: 404,
                type: "https://payment-gateway.example.com/problems/merchant-not-found",
                title: "Merchant not found",
                detail: $"Merchant '{effectiveId}' was not found.",
                instance: httpContext.Request.Path.Value);
        }

        return Results.Ok(response);
    }
}
