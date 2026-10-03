using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaymentGateway.Application.Common;

namespace PaymentGateway.Infrastructure.Fraud;

public sealed class HttpFraudDetectionClient : IFraudDetectionClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpFraudDetectionClient> _logger;

    public HttpFraudDetectionClient(HttpClient httpClient, ILogger<HttpFraudDetectionClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<FraudRiskScore> ScoreAsync(FraudScoreRequest request, CancellationToken cancellationToken)
    {
        var payload = new
        {
            paymentId = request.PaymentId.ToString(),
            merchantId = request.MerchantId.ToString(),
            amount = request.Amount,
            currency = request.Currency,
            cardToken = request.CardToken,
            transactionTime = request.TransactionTime.ToString("O"),
            velocity = new
            {
                sameCardLastHour = request.Velocity.SameCardLastHour,
                sameCardLastDay = request.Velocity.SameCardLastDay,
                sameMerchantLastHour = request.Velocity.SameMerchantLastHour,
                sameCardAmountSumLastDay = request.Velocity.SameCardAmountSumLastDay,
            },
        };

        using var response = await _httpClient.PostAsJsonAsync("/score", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return new FraudRiskScore
        {
            Score = json.GetProperty("score").GetDecimal(),
            ModelVersion = json.GetProperty("modelVersion").GetString() ?? "unknown",
            TopFeatures = null,
        };
    }
}
