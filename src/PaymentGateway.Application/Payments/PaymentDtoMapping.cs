using PaymentGateway.Application.Contracts;
using PaymentGateway.Domain.Entities;

namespace PaymentGateway.Application.Payments;

/// <summary>
/// Maps Payment entities to PaymentResponse DTOs. Never exposes CardToken or any sensitive input.
/// </summary>
public static class PaymentDtoMapping
{
    public static PaymentResponse MapToResponse(Payment payment)
    {
        return new PaymentResponse
        {
            Id = payment.Id,
            MerchantId = payment.MerchantId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString(),
            CreatedAt = payment.CreatedAt,
            AuthCode = payment.AuthCode,
            AcquirerReference = payment.AcquirerReference,
            AuthorizedAt = payment.AuthorizedAt,
            SettledAt = payment.SettledAt,
            FailedAt = payment.FailedAt,
            FailureReason = payment.FailureReason,
            TotalRefunded = payment.TotalRefunded,
        };
    }

    public static CreatePaymentResponse MapToCreateResponse(Payment payment)
    {
        return new CreatePaymentResponse
        {
            PaymentId = payment.Id,
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            CreatedAt = payment.CreatedAt,
        };
    }
}
