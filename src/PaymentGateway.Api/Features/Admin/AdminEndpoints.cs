using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Api.Infrastructure.Authentication;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Ledger;
using PaymentGateway.Application.Persistence;

namespace PaymentGateway.Api.Features.Admin;

/// <summary>
/// Admin endpoints. Require admin API key. Used for operational inspection: reconciliation,
/// poisoned outbox messages, and webhook delivery audit.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Admin");

        group.MapPost("/reconcile", Reconcile);
        group.MapGet("/outbox/poisoned", GetPoisonedMessages);

        return app;
    }

    private static async Task<IResult> Reconcile(
        MerchantContext merchantContext,
        ReconciliationService reconciliationService,
        CancellationToken cancellationToken)
    {
        if (!merchantContext.IsAuthenticated || !merchantContext.IsAdmin)
        {
            return Results.Unauthorized();
        }

        var result = await reconciliationService.ReconcileAllAsync(cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPoisonedMessages(
        MerchantContext merchantContext,
        IPaymentGatewayDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!merchantContext.IsAuthenticated || !merchantContext.IsAdmin)
        {
            return Results.Unauthorized();
        }

        var poisoned = await dbContext.OutboxDeadLetters
            .AsNoTracking()
            .OrderByDescending(d => d.PoisonedAt)
            .Take(100)
            .Select(d => new PoisonedOutboxResponse
            {
                Id = d.Id,
                EventType = d.EventType,
                AggregateId = d.AggregateId,
                Attempts = d.Attempts,
                LastError = d.LastError,
                OriginalOccurredAt = d.OriginalOccurredAt,
                PoisonedAt = d.PoisonedAt,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(poisoned);
    }
}
