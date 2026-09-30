using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Api.Infrastructure.Authentication;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Ledger;

namespace PaymentGateway.Api.Features.Ledger;

/// <summary>
/// Ledger endpoints. Lists ledger transactions and returns a single transaction with its entries.
/// The ledger is append-only — these endpoints are read-only.
/// </summary>
public static class LedgerEndpoints
{
    public static IEndpointRouteBuilder MapLedgerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ledger").WithTags("Ledger");

        group.MapGet("/", ListTransactions);
        group.MapGet("/{transactionId:guid}", GetTransaction);

        return app;
    }

    private static async Task<IResult> ListTransactions(
            int page,
            int pageSize,
            MerchantContext merchantContext,
            LedgerService ledgerService,
            CancellationToken cancellationToken)
    {
        if (!merchantContext.IsAuthenticated || merchantContext.IsAdmin)
        {
            return Results.Unauthorized();
        }

        var result = await ledgerService.ListTransactionsAsync(merchantContext.MerchantId, page, pageSize, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetTransaction(
        Guid transactionId,
        MerchantContext merchantContext,
        LedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        if (!merchantContext.IsAuthenticated || merchantContext.IsAdmin)
        {
            return Results.Unauthorized();
        }

        var response = await ledgerService.GetTransactionAsync(transactionId, merchantContext.MerchantId, cancellationToken);
        if (response is null)
        {
            return Results.Problem(
                statusCode: 404,
                type: "https://payment-gateway.example.com/problems/ledger-transaction-not-found",
                title: "Ledger transaction not found",
                detail: $"Ledger transaction '{transactionId}' was not found.",
                instance: $"/api/v1/ledger/{transactionId}");
        }

        return Results.Ok(response);
    }
}
