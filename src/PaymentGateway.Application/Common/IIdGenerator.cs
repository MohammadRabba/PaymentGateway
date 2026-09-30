namespace PaymentGateway.Application.Common;

/// <summary>
/// ID generator abstraction. Production uses sequential GUIDs (for SQL Server index locality);
/// tests use deterministic generators so assertions can predict exact IDs.
/// </summary>
public interface IIdGenerator
{
    Guid NewId();
}
