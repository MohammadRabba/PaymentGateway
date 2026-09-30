using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Common;
using PaymentGateway.Domain.Enums;

namespace PaymentGateway.Infrastructure.Payments;

/// <summary>
/// Simulated acquirer. Realistic but deterministic behaviour:
///   - AuthorizeAsync: applies the configured failure mode, sleeps Latency, returns outcome.
///   - GetAuthorizationStatusAsync: returns the original outcome if TrackAuthorizations=true,
///     otherwise Unknown. This enables the recovery worker to resolve Unknown-state payments
///     WITHOUT re-issuing an authorization request.
///   - RefundAsync: simpler — by default succeeds unless failure mode is configured.
///
/// The simulator tracks authorizations in a ConcurrentDictionary keyed by idempotency key so that
/// the deterministic outcome is reproducible across calls. This is critical for the Unknown-state
/// recovery flow: when a payment times out, the recovery worker queries the same key and the
/// simulator returns the original outcome.
/// </summary>
public sealed class SimulatedAcquirerClient : IAcquirerClient
{
    private readonly AcquirerOptions _options;
    private readonly ILogger<SimulatedAcquirerClient> _logger;
    private readonly System.Random _random;

    /// <summary>
    /// Authorizations tracked by idempotency key. Used by GetAuthorizationStatusAsync to resolve
    /// Unknown-state payments without re-issuing the original request.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, AcquirerAuthorization> _authorizations = new();

    public SimulatedAcquirerClient(IOptions<AcquirerOptions> options, ILogger<SimulatedAcquirerClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        // Deterministic RNG — same seed + same call sequence = same outcomes.
        _random = new System.Random(_options.Seed);
    }

    public async Task<AcquirerAuthorization> AuthorizeAsync(AcquirerAuthorizationRequest request, CancellationToken cancellationToken)
    {
        // Simulated latency. Honor cancellation.
        await Task.Delay(_options.Latency, cancellationToken);

        // If this idempotency key was already used, return the original outcome (acquirer idempotency).
        // This prevents double-authorization in the recovery flow when the recovery worker calls
        // AuthorizeAsync (it should NOT, but if it did, the acquirer would return the original).
        if (_options.TrackAuthorizations && _authorizations.TryGetValue(request.IdempotencyKey, out var existing))
        {
            _logger.LogInformation("SimulatedAcquirer: returning cached authorization for key {Key}", request.IdempotencyKey);
            return existing;
        }

        var outcome = ResolveOutcome();
        var authCode = GenerateAuthCode();
        var acquirerReference = GenerateAcquirerReference(request.PaymentId);

        var result = new AcquirerAuthorization
        {
            AuthCode = authCode,
            AcquirerReference = acquirerReference,
            Outcome = outcome,
            DeclineReason = outcome == AcquirerOutcome.Decline ? "Simulated decline" : null,
        };

        if (_options.TrackAuthorizations)
        {
            _authorizations[request.IdempotencyKey] = result;
        }

        switch (_options.FailureMode)
        {
            case AcquirerFailureMode.Transient5xx:
                _logger.LogWarning("SimulatedAcquirer: throwing transient 5xx for payment {PaymentId}", request.PaymentId);
                throw new HttpRequestException("Simulated acquirer 5xx error");
            case AcquirerFailureMode.Timeout:
                _logger.LogWarning("SimulatedAcquirer: throwing timeout for payment {PaymentId}", request.PaymentId);
                throw new TaskCanceledException("Simulated acquirer timeout");
            case AcquirerFailureMode.Decline:
                _logger.LogInformation("SimulatedAcquirer: declining payment {PaymentId}", request.PaymentId);
                break;
            case AcquirerFailureMode.Unknown:
                _logger.LogInformation("SimulatedAcquirer: unknown outcome for payment {PaymentId}", request.PaymentId);
                result = result with { Outcome = AcquirerOutcome.Unknown };
                break;
            default:
                _logger.LogInformation("SimulatedAcquirer: authorized payment {PaymentId} authCode {AuthCode}", request.PaymentId, authCode);
                break;
        }

        return result;
    }

    public Task<AcquirerAuthorization> GetAuthorizationStatusAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        if (_options.TrackAuthorizations && _authorizations.TryGetValue(idempotencyKey, out var existing))
        {
            _logger.LogInformation("SimulatedAcquirer: returning cached authorization status for key {Key}", idempotencyKey);
            return Task.FromResult(existing);
        }

        // Key not found — return Unknown. The recovery worker will treat this as "still unknown"
        // and retry later, eventually force-failing after MaxRecoveryAttempts.
        _logger.LogWarning("SimulatedAcquirer: no cached authorization for key {Key}; returning Unknown", idempotencyKey);
        return Task.FromResult(new AcquirerAuthorization
        {
            AuthCode = string.Empty,
            AcquirerReference = string.Empty,
            Outcome = AcquirerOutcome.Unknown,
            DeclineReason = "No record of this authorization",
        });
    }

    public async Task<AcquirerRefundResult> RefundAsync(AcquirerRefundRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(_options.Latency, cancellationToken);

        // For simplicity, refunds succeed unless the failure mode is Decline.
        if (_options.FailureMode == AcquirerFailureMode.Decline)
        {
            return new AcquirerRefundResult
            {
                RefundReference = string.Empty,
                Outcome = AcquirerOutcome.Decline,
                DeclineReason = "Simulated refund decline",
            };
        }

        return new AcquirerRefundResult
        {
            RefundReference = GenerateRefundReference(request.RefundId),
            Outcome = AcquirerOutcome.Success,
            DeclineReason = null,
        };
    }

    private AcquirerOutcome ResolveOutcome()
    {
        if (_options.FailureMode == AcquirerFailureMode.Random)
        {
            // Deterministic pseudo-random outcome.
            lock (_random)
            {
                return _random.NextDouble() < _options.FailureProbability
                    ? AcquirerOutcome.Decline
                    : AcquirerOutcome.Success;
            }
        }

        // For Decline mode, the outcome is Decline.
        return _options.FailureMode == AcquirerFailureMode.Decline
            ? AcquirerOutcome.Decline
            : AcquirerOutcome.Success;
    }

    private static string GenerateAuthCode()
    {
        // 6-digit auth code, like real acquirers return.
        var bytes = new byte[3];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return $"{bytes[0]:X2}{bytes[1]:X2}{bytes[2]:X2}";
    }

    private static string GenerateAcquirerReference(Guid paymentId)
    {
        return $"ACQ-{paymentId:N}".ToUpperInvariant();
    }

    private static string GenerateRefundReference(Guid refundId)
    {
        return $"RFND-{refundId:N}".ToUpperInvariant();
    }
}
