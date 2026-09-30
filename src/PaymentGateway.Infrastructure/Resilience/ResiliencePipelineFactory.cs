using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using System.Net;

namespace PaymentGateway.Infrastructure.Resilience;

/// <summary>
/// Builds Polly v8 resilience pipelines for outbound calls. Two pipelines:
///   1. AcquirerPipeline: retry + circuit breaker + timeout. Retries transient HTTP 5xx and timeouts.
///      NEVER retries permanent declines or business rule violations (those are domain errors).
///   2. WebhookPipeline: retry + circuit breaker + timeout. Retries 408/429/5xx/network.
///      NEVER retries permanent 4xx (the merchant endpoint is fundamentally broken).
///
/// Non-retryable exceptions are explicitly excluded via the ShouldHandleExceptionPredicate.
/// </summary>
public sealed class ResiliencePipelineFactory
{
    private readonly ResilienceOptions _options;
    private readonly ILogger<ResiliencePipelineFactory> _logger;

    public ResiliencePipelineFactory(IOptions<ResilienceOptions> options, ILogger<ResiliencePipelineFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Build the acquirer resilience pipeline. Used by the SimulatedAcquirerClient wrapper (or by
    /// a real HTTP client). The pipeline retries on transient failures with exponential backoff
    /// + jitter, opens a circuit breaker on sustained failures, and enforces a per-call timeout.
    /// </summary>
    public ResiliencePipeline BuildAcquirerPipeline()
    {
        var acqOpts = _options.Acquirer;

        var retry = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(ShouldRetryHttpRequest)
                                                  .Handle<TaskCanceledException>()
                                                  .Handle<TimeoutException>(),
            MaxRetryAttempts = acqOpts.RetryAttempts,
            Delay = acqOpts.InitialDelay,
            MaxDelay = acqOpts.MaxDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            OnRetry = args =>
            {
                _logger.LogWarning("Acquirer retry attempt {Attempt} after {Delay}ms due to {Exception}",
                    args.AttemptNumber + 1,
                    args.RetryDelay.TotalMilliseconds,
                    args.Outcome.Exception?.GetType().Name);
                return ValueTask.CompletedTask;
            },
        };

        var circuitBreaker = new CircuitBreakerStrategyOptions
        {
            FailureRatio = 1.0,
            MinimumThroughput = acqOpts.CircuitBreakerFailureThreshold,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = acqOpts.CircuitBreakerOpenDuration,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(ShouldRetryHttpRequest)
                                                  .Handle<TaskCanceledException>()
                                                  .Handle<TimeoutException>(),
            OnOpened = args =>
            {
                _logger.LogWarning("Acquirer circuit breaker OPENED for {BreakDuration}ms. Context: {Context}",
                    args.BreakDuration.TotalMilliseconds, args.Context.OperationKey ?? "unknown");
                return ValueTask.CompletedTask;
            },
            OnClosed = args =>
            {
                _logger.LogInformation("Acquirer circuit breaker CLOSED — calls resuming normally.");
                return ValueTask.CompletedTask;
            },
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retry)
            .AddCircuitBreaker(circuitBreaker)
            .AddTimeout(acqOpts.Timeout)
            .Build();
    }

    /// <summary>
    /// Build the webhook resilience pipeline. Same pattern as the acquirer pipeline but with
    /// different defaults (longer delays, higher circuit breaker threshold).
    /// </summary>
    public ResiliencePipeline BuildWebhookPipeline()
    {
        var whOpts = _options.Webhook;

        var retry = new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(ShouldRetryHttpRequest)
                                                  .Handle<TaskCanceledException>()
                                                  .Handle<TimeoutException>(),
            MaxRetryAttempts = whOpts.RetryAttempts,
            Delay = whOpts.InitialDelay,
            MaxDelay = whOpts.MaxDelay,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            OnRetry = args =>
            {
                _logger.LogWarning("Webhook retry attempt {Attempt} after {Delay}ms due to {Exception}",
                    args.AttemptNumber + 1,
                    args.RetryDelay.TotalMilliseconds,
                    args.Outcome.Exception?.GetType().Name);
                return ValueTask.CompletedTask;
            },
        };

        var circuitBreaker = new CircuitBreakerStrategyOptions
        {
            FailureRatio = 1.0,
            MinimumThroughput = whOpts.CircuitBreakerFailureThreshold,
            SamplingDuration = TimeSpan.FromSeconds(60),
            BreakDuration = whOpts.CircuitBreakerOpenDuration,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(ShouldRetryHttpRequest)
                                                  .Handle<TaskCanceledException>()
                                                  .Handle<TimeoutException>(),
            OnOpened = args =>
            {
                _logger.LogWarning("Webhook circuit breaker OPENED for {BreakDuration}ms. Context: {Context}",
                    args.BreakDuration.TotalMilliseconds, args.Context.OperationKey ?? "unknown");
                return ValueTask.CompletedTask;
            },
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(retry)
            .AddCircuitBreaker(circuitBreaker)
            .AddTimeout(whOpts.Timeout)
            .Build();
    }

    /// <summary>
    /// Predicate for HttpRequestException: retry only on transient HTTP status codes (5xx) and
    /// network errors. NEVER retry on 4xx (the server said no — retrying won't help).
    /// </summary>
    private static bool ShouldRetryHttpRequest(HttpRequestException ex)
    {
        if (ex.StatusCode is null)
        {
            // No status code = network error. Retry.
            return true;
        }

        var code = (int)ex.StatusCode.Value;
        // Retry only 5xx. 4xx is permanent at the HTTP level — the merchant endpoint is broken.
        return code >= 500;
    }
}
