using FluentAssertions;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Common;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Payments;

namespace PaymentGateway.UnitTests.Infrastructure;

public class SimulatedAcquirerClientTests
{
    private static SimulatedAcquirerClient CreateClient(AcquirerFailureMode mode) =>
        new(Options.Create(new AcquirerOptions
        {
            FailureMode = mode,
            Latency = TimeSpan.FromMilliseconds(1),
            Seed = 42,
            TrackAuthorizations = true,
        }), Microsoft.Extensions.Logging.Abstractions.NullLogger<SimulatedAcquirerClient>.Instance);

    private static AcquirerAuthorizationRequest NewRequest(Guid paymentId) => new()
    {
        PaymentId = paymentId,
        Amount = 100m,
        Currency = "USD",
        CardToken = "tok_test",
        IdempotencyKey = $"PAY-{paymentId:N}",
    };

    [Fact]
    public async Task AuthorizeAsync_returns_Success_when_failure_mode_is_None()
    {
        var client = CreateClient(AcquirerFailureMode.None);
        var result = await client.AuthorizeAsync(NewRequest(Guid.NewGuid()), CancellationToken.None);

        result.Outcome.Should().Be(AcquirerOutcome.Success);
        result.AuthCode.Should().NotBeNullOrEmpty();
        result.AcquirerReference.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AuthorizeAsync_returns_Decline_when_failure_mode_is_Decline()
    {
        var client = CreateClient(AcquirerFailureMode.Decline);
        var result = await client.AuthorizeAsync(NewRequest(Guid.NewGuid()), CancellationToken.None);

        result.Outcome.Should().Be(AcquirerOutcome.Decline);
        result.DeclineReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AuthorizeAsync_throws_HttpRequestException_when_mode_is_Transient5xx()
    {
        var client = CreateClient(AcquirerFailureMode.Transient5xx);
        var act = async () => await client.AuthorizeAsync(NewRequest(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AuthorizeAsync_throws_TaskCanceledException_when_mode_is_Timeout()
    {
        var client = CreateClient(AcquirerFailureMode.Timeout);
        var act = async () => await client.AuthorizeAsync(NewRequest(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    [Fact]
    public async Task AuthorizeAsync_returns_Unknown_when_mode_is_Unknown()
    {
        var client = CreateClient(AcquirerFailureMode.Unknown);
        var result = await client.AuthorizeAsync(NewRequest(Guid.NewGuid()), CancellationToken.None);
        result.Outcome.Should().Be(AcquirerOutcome.Unknown);
    }

    [Fact]
    public async Task GetAuthorizationStatusAsync_returns_cached_outcome_for_known_key()
    {
        var client = CreateClient(AcquirerFailureMode.None);
        var paymentId = Guid.NewGuid();
        var request = NewRequest(paymentId);

        var original = await client.AuthorizeAsync(request, CancellationToken.None);
        var status = await client.GetAuthorizationStatusAsync(request.IdempotencyKey, CancellationToken.None);

        status.Outcome.Should().Be(original.Outcome);
        status.AuthCode.Should().Be(original.AuthCode);
    }

    [Fact]
    public async Task GetAuthorizationStatusAsync_returns_Unknown_for_unknown_key()
    {
        var client = CreateClient(AcquirerFailureMode.None);
        var status = await client.GetAuthorizationStatusAsync("non-existent-key", CancellationToken.None);
        status.Outcome.Should().Be(AcquirerOutcome.Unknown);
    }

    [Fact]
    public async Task AuthorizeAsync_returns_cached_outcome_for_repeat_idempotency_key()
    {
        var client = CreateClient(AcquirerFailureMode.None);
        var request = NewRequest(Guid.NewGuid());

        var first = await client.AuthorizeAsync(request, CancellationToken.None);
        var second = await client.AuthorizeAsync(request, CancellationToken.None);

        second.AuthCode.Should().Be(first.AuthCode);
        second.AcquirerReference.Should().Be(first.AcquirerReference);
    }

    [Fact]
    public async Task RefundAsync_returns_Success_when_failure_mode_is_None()
    {
        var client = CreateClient(AcquirerFailureMode.None);
        var request = new AcquirerRefundRequest
        {
            PaymentId = Guid.NewGuid(),
            RefundId = Guid.NewGuid(),
            OriginalAcquirerReference = "ACQ-123",
            Amount = 50m,
            Currency = "USD",
            IdempotencyKey = "REF-123",
        };

        var result = await client.RefundAsync(request, CancellationToken.None);
        result.Outcome.Should().Be(AcquirerOutcome.Success);
        result.RefundReference.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefundAsync_returns_Decline_when_failure_mode_is_Decline()
    {
        var client = CreateClient(AcquirerFailureMode.Decline);
        var request = new AcquirerRefundRequest
        {
            PaymentId = Guid.NewGuid(),
            RefundId = Guid.NewGuid(),
            OriginalAcquirerReference = "ACQ-123",
            Amount = 50m,
            Currency = "USD",
            IdempotencyKey = "REF-123",
        };

        var result = await client.RefundAsync(request, CancellationToken.None);
        result.Outcome.Should().Be(AcquirerOutcome.Decline);
    }

    [Fact]
    public async Task AuthorizeAsync_with_Random_mode_produces_deterministic_outcomes_for_same_seed()
    {
        var client1 = CreateClient(AcquirerFailureMode.Random);
        var client2 = CreateClient(AcquirerFailureMode.Random);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        // Two clients with same seed should produce same outcome for the same first call sequence.
        // We can't predict whether it's success or decline, but they should match.
        var r1 = await client1.AuthorizeAsync(NewRequest(id1), CancellationToken.None);
        var r2 = await client2.AuthorizeAsync(NewRequest(id1), CancellationToken.None);

        r1.Outcome.Should().Be(r2.Outcome);
    }
}
