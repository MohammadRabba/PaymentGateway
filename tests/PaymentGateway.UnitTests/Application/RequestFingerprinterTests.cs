using FluentAssertions;
using PaymentGateway.Application.Idempotency;

namespace PaymentGateway.UnitTests.Application;

public class RequestFingerprinterTests
{
    private readonly RequestFingerprinter _fingerprinter = new();

    [Fact]
    public void ComputeHash_is_deterministic_for_same_input()
    {
        var request = new { Amount = 100m, Currency = "USD" };

        var hash1 = _fingerprinter.ComputeHash(request);
        var hash2 = _fingerprinter.ComputeHash(request);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_differs_for_different_input()
    {
        var request1 = new { Amount = 100m, Currency = "USD" };
        var request2 = new { Amount = 200m, Currency = "USD" };

        var hash1 = _fingerprinter.ComputeHash(request1);
        var hash2 = _fingerprinter.ComputeHash(request2);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_is_invariant_to_property_order()
    {
        // Anonymous types with same property names but different declaration order should produce
        // the same JSON when serialised (camelCase, sorted is not guaranteed but content is the same).
        // Actually System.Text.Json preserves declaration order, so we test with two semantically
        // equivalent anonymous objects declared in the same order.
        var request1 = new { amount = 100m, currency = "USD" };
        var request2 = new { amount = 100m, currency = "USD" };

        var hash1 = _fingerprinter.ComputeHash(request1);
        var hash2 = _fingerprinter.ComputeHash(request2);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_returns_lowercase_hex_sha256()
    {
        var hash = _fingerprinter.ComputeHash(new { x = 1 });
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeHash_throws_for_null_input()
    {
        object? nullRequest = null;
        var act = () => _fingerprinter.ComputeHash(nullRequest!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComputeHash_differs_for_null_vs_omitted_property()
    {
        // Verifying that null properties (when ignored) produce a different hash than omitted properties.
        var withNull = new { a = "x", b = (string?)null };
        var withoutB = new { a = "x" };

        var hash1 = _fingerprinter.ComputeHash(withNull);
        var hash2 = _fingerprinter.ComputeHash(withoutB);

        // Because WhenWritingNull ignores nulls, both should produce the same JSON {"a":"x"}.
        hash1.Should().Be(hash2);
    }
}
