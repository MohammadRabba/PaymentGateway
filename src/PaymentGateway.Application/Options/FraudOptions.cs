namespace PaymentGateway.Application.Options;

public sealed class FraudOptions
{
    public const string SectionName = "Fraud";

    /// <summary>Master switch. If false, the risk gate is skipped entirely.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Score >= this ? Block the payment. Default 0.96 (your notebook's max-F1 threshold).</summary>
    public decimal BlockThreshold { get; init; } = 0.96m;

    /// <summary>Score >= this and < BlockThreshold ? flag for review but proceed.</summary>
    public decimal ReviewThreshold { get; init; } = 0.50m;

    /// <summary>If the fraud service is unavailable, Block (true) or Pass (false).</summary>
    public FailClosedMode FailClosedMode { get; init; } = FailClosedMode.Pass;

    /// <summary>HTTP timeout for the fraud service call.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Endpoint URL for the Python fraud service. Empty = use ONNX in-process.</summary>
    public string ServiceUrl { get; init; } = string.Empty;

    /// <summary>Path to the ONNX model file (only used if ServiceUrl is empty).</summary>
    public string OnnxModelPath { get; init; } = string.Empty;
}

public enum FailClosedMode
{
    /// <summary>Fraud service down ? allow the payment, emit alert.</summary>
    Pass = 1,

    /// <summary>Fraud service down ? block the payment, emit alert.</summary>
    Block = 2,
}
