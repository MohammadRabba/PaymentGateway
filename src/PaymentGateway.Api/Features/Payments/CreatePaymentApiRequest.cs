using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Features.Payments;

/// <summary>
/// API request body for POST /api/v1/payments. The IdempotencyKey is extracted from the
/// X-Idempotency-Key header by the endpoint and passed into the Application DTO. The MerchantId
/// is set from the authenticated merchant context, never from the request body.
/// </summary>
public sealed class CreatePaymentApiRequest
{
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be positive.")]
    public decimal Amount { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be exactly 3 letters.")]
    public string Currency { get; init; } = string.Empty;

    [Required]
    [StringLength(64, MinimumLength = 4, ErrorMessage = "CardToken must be 4-64 characters.")]
    public string CardToken { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }
}

public sealed class RefundApiRequest
{
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be positive.")]
    public decimal Amount { get; init; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Reason { get; init; }
}
