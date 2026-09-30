namespace PaymentGateway.Infrastructure.Observability;

/// <summary>
/// Logging configuration. Serilog enrichers and sinks are configured here.
/// </summary>
public sealed class LoggingOptions
{
    public const string SectionName = "Logging:Structured";

    /// <summary>Minimum log level. Default Information; set to Debug for verbose tracing.</summary>
    public string MinimumLevel { get; init; } = "Information";

    /// <summary>If true, write structured logs to console in JSON format (production).</summary>
    public bool ConsoleJson { get; init; } = false;

    /// <summary>If true, write human-readable colored console output (development).</summary>
    public bool ConsoleHumanReadable { get; init; } = true;

    /// <summary>Optional Seq server URL for log aggregation.</summary>
    public string? SeqServerUrl { get; init; }

    /// <summary>Maximum size of log message body in characters. Longer bodies are truncated.</summary>
    public int MaxMessageBodyLength { get; init; } = 4000;
}
