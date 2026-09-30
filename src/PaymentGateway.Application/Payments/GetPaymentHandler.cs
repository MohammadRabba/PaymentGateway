using Microsoft.EntityFrameworkCore;
using PaymentGateway.Application.Contracts;
using PaymentGateway.Application.Exceptions;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Payments;

/// <summary>
/// Reads a payment by ID. Enforces merchant isolation: a merchant cannot read another merchant's
/// payment. Uses AsNoTracking for read-only performance.
/// </summary>
public sealed class GetPaymentHandler
{
    private readonly IPaymentGatewayDbContext _dbContext;

    public GetPaymentHandler(IPaymentGatewayDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PaymentResponse> HandleAsync(Guid paymentId, Guid merchantId, CancellationToken cancellationToken)
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

        return PaymentDtoMapping.MapToResponse(payment);
    }
}
