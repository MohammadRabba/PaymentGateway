using System.Globalization;

namespace PaymentGateway.Domain.ValueObjects;

/// <summary>
/// ISO 4217-style currency code. Stored as a 3-character uppercase string but represented
/// in the domain as a strongly typed value to prevent accidental misuse of raw strings.
/// Different currencies have different minor-unit precision; the precision is exposed via
/// <see cref="DecimalPlaces"/> so <see cref="Money"/> rounding can honour it.
/// </summary>
public readonly record struct Currency
{
    public string Code { get; }

    public int DecimalPlaces { get; }

    private Currency(string code, int decimalPlaces)
    {
        Code = code;
        DecimalPlaces = decimalPlaces;
    }

    /// <summary>
    /// Supported currencies with their ISO 4217 minor-unit precision. Three-letter codes only.
    /// The set is intentionally small for this simulation; extend as required.
    /// </summary>
    private static readonly Dictionary<string, int> Supported = new(StringComparer.Ordinal)
    {
        ["USD"] = 2,
        ["EUR"] = 2,
        ["GBP"] = 2,
        ["CHF"] = 2,
        ["CAD"] = 2,
        ["AUD"] = 2,
        ["JPY"] = 0,
        ["KWD"] = 3,
        ["BHD"] = 3,
        ["OMR"] = 3,
    };

    public static Currency Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Currency code must be a non-empty 3-letter ISO code.", nameof(input));
        }

        var normalized = input.ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetter))
        {
            throw new ArgumentException($"Currency code '{input}' must be exactly 3 uppercase ASCII letters.", nameof(input));
        }

        if (!Supported.TryGetValue(normalized, out var decimals))
        {
            throw new ArgumentException($"Currency code '{normalized}' is not supported.", nameof(input));
        }

        return new Currency(normalized, decimals);
    }

    public static bool TryParse(string input, out Currency currency)
    {
        try
        {
            currency = Parse(input);
            return true;
        }
        catch (ArgumentException)
        {
            currency = default;
            return false;
        }
    }

    public static Currency USD => new("USD", 2);
    public static Currency EUR => new("EUR", 2);
    public static Currency GBP => new("GBP", 2);
    public static Currency JPY => new("JPY", 0);

    /// <summary>True if a currency code is in the supported set.</summary>
    public static bool IsSupported(string code) =>
        !string.IsNullOrWhiteSpace(code)
        && Supported.ContainsKey(code.ToUpperInvariant());

    public override string ToString() => Code;

    public static bool operator ==(Currency left, string right) =>
        string.Equals(left.Code, right, StringComparison.Ordinal);

    public static bool operator !=(Currency left, string right) =>
        !(left == right);

    public static bool operator ==(string left, Currency right) => right == left;

    public static bool operator !=(string left, Currency right) => !(left == right);
}
