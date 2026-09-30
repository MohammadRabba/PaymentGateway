using PaymentGateway.Application.Contracts;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Refunds;

/// <summary>
/// Maps Refund entities to RefundResponse DTOs.
/// </summary>
public static class RefundDtoMapping
{
    public static RefundResponse MapToResponse(Refund refund)
    {
        return new RefundResponse
        {
            RefundId = refund.Id,
            PaymentId = refund.PaymentId,
            Amount = refund.Amount,
            Currency = refund.Currency,
            Status = refund.Status.ToString(),
            CreatedAt = refund.CreatedAt,
            CompletedAt = refund.CompletedAt,
            FailureReason = refund.FailureReason,
        };
    }
}
