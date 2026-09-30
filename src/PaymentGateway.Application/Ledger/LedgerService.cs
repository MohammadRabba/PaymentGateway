using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Application.Ledger;

/// <summary>
/// Read-only ledger query service. Used by GET endpoints to return ledger transactions and entries.
/// The ledger is append-only — this service has no mutation methods.
/// </summary>
public sealed class LedgerService
{
    private readonly IPaymentGatewayDbContext _dbContext;

    public LedgerService(IPaymentGatewayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<LedgerTransactionResponse?> GetTransactionAsync(
        Guid transactionId,
        Guid merchantId,
        CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.LedgerTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == transactionId, cancellationToken);

        if (transaction is null)
        {
            return null;
        }

        // Merchant isolation: the transaction must belong to the authenticated merchant.
        // A ledger transaction is linked to a payment or refund, both of which carry MerchantId.
        if (transaction.PaymentId is not null)
        {
            var payment = await _dbContext.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == transaction.PaymentId, cancellationToken);
            if (payment is null || payment.MerchantId != merchantId)
            {
                return null;
            }
        }
        else if (transaction.RefundId is not null)
        {
            var refund = await _dbContext.Refunds
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == transaction.RefundId, cancellationToken);
            if (refund is null || refund.MerchantId != merchantId)
            {
                return null;
            }
        }

        var entries = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(e => e.LedgerTransactionId == transactionId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return MapToResponse(transaction, entries);
    }

    public async Task<PagedResult<LedgerTransactionResponse>> ListTransactionsAsync(
        Guid merchantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var paymentIds = await _dbContext.Payments
            .AsNoTracking()
            .Where(p => p.MerchantId == merchantId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var refundIds = await _dbContext.Refunds
            .AsNoTracking()
            .Where(r => r.MerchantId == merchantId)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var query = _dbContext.LedgerTransactions
            .AsNoTracking()
            .Where(t => (t.PaymentId != null && paymentIds.Contains(t.PaymentId.Value))
                      || (t.RefundId != null && refundIds.Contains(t.RefundId.Value)));

        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var transactionIds = transactions.Select(t => t.Id).ToList();
        var entries = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(e => transactionIds.Contains(e.LedgerTransactionId))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var items = transactions
            .Select(t => MapToResponse(t, entries.Where(e => e.LedgerTransactionId == t.Id).ToList()))
            .ToList();

        return new PagedResult<LedgerTransactionResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<IReadOnlyList<LedgerEntryResponse>> GetEntriesForPaymentAsync(
        Guid paymentId,
        Guid merchantId,
        CancellationToken cancellationToken)
    {
        var payment = await _dbContext.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentNotFoundException(paymentId);
        }
        if (payment.MerchantId != merchantId)
        {
            throw new MerchantIsolationException(merchantId, payment.MerchantId);
        }

        var transactionIds = await _dbContext.LedgerTransactions
            .AsNoTracking()
            .Where(t => t.PaymentId == paymentId)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var entries = await _dbContext.LedgerEntries
            .AsNoTracking()
            .Where(e => transactionIds.Contains(e.LedgerTransactionId))
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return entries.Select(MapEntryToResponse).ToList();
    }

    private static LedgerTransactionResponse MapToResponse(LedgerTransaction transaction, IReadOnlyList<LedgerEntry> entries)
    {
        return new LedgerTransactionResponse
        {
            Id = transaction.Id,
            PaymentId = transaction.PaymentId,
            RefundId = transaction.RefundId,
            Type = transaction.Type.ToString(),
            Currency = transaction.Currency,
            CorrelationId = transaction.CorrelationId,
            CreatedAt = transaction.CreatedAt,
            Entries = entries.Select(MapEntryToResponse).ToList(),
        };
    }

    private static LedgerEntryResponse MapEntryToResponse(LedgerEntry entry)
    {
        return new LedgerEntryResponse
        {
            Id = entry.Id,
            AccountId = entry.AccountId,
            EntryType = entry.EntryType.ToString(),
            Amount = entry.Amount,
            Currency = entry.Currency,
            CreatedAt = entry.CreatedAt,
        };
    }
}
