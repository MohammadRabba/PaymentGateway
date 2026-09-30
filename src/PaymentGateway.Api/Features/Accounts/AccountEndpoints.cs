using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentGateway.Api.Infrastructure.Authentication;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Persistence;

namespace PaymentGateway.Api.Features.Accounts;

/// <summary>
/// Account endpoints. Returns merchant accounts and balances. A merchant can only access their
/// own accounts; admin can access any account by ID.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/accounts").WithTags("Accounts");

        group.MapGet("/", ListAccounts);
        group.MapGet("/{accountId:guid}", GetAccount);

        return app;
    }

    private static async Task<IResult> ListAccounts(
        MerchantContext merchantContext,
        IPaymentGatewayDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!merchantContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        if (merchantContext.IsAdmin)
        {
            return Results.BadRequest("Admin must specify a merchant context. Use GET /api/v1/merchants first.");
        }

        var accounts = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.MerchantId == merchantContext.MerchantId)
            .OrderBy(a => a.AccountType)
            .ThenBy(a => a.Currency)
            .Select(a => new AccountResponse
            {
                Id = a.Id,
                MerchantId = a.MerchantId,
                AccountType = a.AccountType.ToString(),
                Currency = a.Currency,
                Balance = a.Balance,
                Status = a.Status.ToString(),
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(accounts);
    }

    private static async Task<IResult> GetAccount(
        Guid accountId,
        MerchantContext merchantContext,
        IPaymentGatewayDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!merchantContext.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account is null)
        {
            return Results.Problem(
                statusCode: 404,
                type: "https://payment-gateway.example.com/problems/account-not-found",
                title: "Account not found",
                detail: $"Account '{accountId}' was not found.",
                instance: $"/api/v1/accounts/{accountId}");
        }

        if (!merchantContext.IsAdmin && account.MerchantId != merchantContext.MerchantId)
        {
            throw new MerchantIsolationException(merchantContext.MerchantId, account.MerchantId);
        }

        var response = new AccountResponse
        {
            Id = account.Id,
            MerchantId = account.MerchantId,
            AccountType = account.AccountType.ToString(),
            Currency = account.Currency,
            Balance = account.Balance,
            Status = account.Status.ToString(),
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
        };

        return Results.Ok(response);
    }
}
