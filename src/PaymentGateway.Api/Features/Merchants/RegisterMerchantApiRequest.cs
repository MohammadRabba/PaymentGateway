using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Features.Merchants;

/// <summary>
/// API request body for POST /api/v1/merchants (admin only). The IdempotencyKey is extracted from
/// the X-Idempotency-Key header by the endpoint.
/// </summary>
public sealed class RegisterMerchantApiRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ExternalReference { get; init; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(2048, MinimumLength = 8)]
    [Url(ErrorMessage = "WebhookUrl must be a valid URL.")]
    public string WebhookUrl { get; init; } = string.Empty;
}
