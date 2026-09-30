namespace PaymentGateway.Domain.Exceptions;

/// <summary>
/// Thrown when a posted ledger transaction's debits do not equal credits.
/// This indicates a programming error in posting logic — never a user-correctable input.
/// The transaction must roll back entirely.
/// </summary>
public sealed class LedgerNotBalancedException : DomainException
{
    public decimal TotalDebits { get; }

    public decimal TotalCredits { get; }

    public LedgerNotBalancedException(decimal totalDebits, decimal totalCredits)
        : base($"Ledger not balanced: debits {totalDebits} do not equal credits {totalCredits}.")
    {
        TotalDebits = totalDebits;
        TotalCredits = totalCredits;
    }

    public override string ProblemTypeUri => "https://payment-gateway.example.com/problems/ledger-not-balanced";
    public override int HttpStatusCode => 500;
}
