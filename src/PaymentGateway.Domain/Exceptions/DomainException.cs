namespace PaymentGateway.Domain.Exceptions;

/// <summary>
/// Base class for all domain rule violations. Caught by the global API handler and mapped
/// to ProblemDetails with a stable error type URI.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string message)
        : base(message)
    {
    }

    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Stable problem type URI used as the ProblemDetails "type" field.
    /// </summary>
    public abstract string ProblemTypeUri { get; }

    /// <summary>
    /// HTTP status to map to. Default 422 Unprocessable Entity for domain rule violations.
    /// </summary>
    public virtual int HttpStatusCode => 422;
}
