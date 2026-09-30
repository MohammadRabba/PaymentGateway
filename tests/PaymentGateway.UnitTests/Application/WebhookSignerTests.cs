using FluentAssertions;
using PaymentGateway.Application.Webhooks;

namespace PaymentGateway.UnitTests.Application;

public class WebhookSignerTests
{
    private readonly WebhookSigner _signer = new();

    [Fact]
    public void ComputeSignature_is_deterministic_for_same_inputs()
    {
        var secret = "test-secret";
        var webhookId = "abc123";
        var timestamp = 1700000000L;
        var payload = "hello world"u8.ToArray();

        var sig1 = _signer.ComputeSignature(secret, webhookId, timestamp, payload);
        var sig2 = _signer.ComputeSignature(secret, webhookId, timestamp, payload);

        sig1.Should().Be(sig2);
    }

    [Fact]
    public void ComputeSignature_changes_when_payload_changes()
    {
        var secret = "test-secret";
        var webhookId = "abc123";
        var timestamp = 1700000000L;

        var sig1 = _signer.ComputeSignature(secret, webhookId, timestamp, "payload1"u8.ToArray());
        var sig2 = _signer.ComputeSignature(secret, webhookId, timestamp, "payload2"u8.ToArray());

        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void ComputeSignature_changes_when_secret_changes()
    {
        var payload = "hello"u8.ToArray();
        var sig1 = _signer.ComputeSignature("secret1", "id", 1L, payload);
        var sig2 = _signer.ComputeSignature("secret2", "id", 1L, payload);
        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void ComputeSignature_returns_lowercase_hex()
    {
        var sig = _signer.ComputeSignature("secret", "id", 1L, "payload"u8.ToArray());
        sig.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void VerifySignature_returns_true_for_correct_signature()
    {
        var secret = "secret";
        var webhookId = "id";
        var timestamp = 1L;
        var payload = "payload"u8.ToArray();
        var signature = _signer.ComputeSignature(secret, webhookId, timestamp, payload);

        _signer.VerifySignature(secret, webhookId, timestamp, payload, signature).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_returns_false_for_tampered_payload()
    {
        var secret = "secret";
        var signature = _signer.ComputeSignature(secret, "id", 1L, "original"u8.ToArray());

        _signer.VerifySignature(secret, "id", 1L, "tampered"u8.ToArray(), signature).Should().BeFalse();
    }

    [Fact]
    public void IsTimestampValid_returns_true_within_tolerance()
    {
        var now = 1700000000L;
        var tolerance = 300L;
        _signer.IsTimestampValid(1700000100L, now, tolerance).Should().BeTrue();
        _signer.IsTimestampValid(1699999900L, now, tolerance).Should().BeTrue();
    }

    [Fact]
    public void IsTimestampValid_returns_false_outside_tolerance()
    {
        var now = 1700000000L;
        var tolerance = 300L;
        _signer.IsTimestampValid(1700000500L, now, tolerance).Should().BeFalse();
        _signer.IsTimestampValid(1699999500L, now, tolerance).Should().BeFalse();
    }
}
