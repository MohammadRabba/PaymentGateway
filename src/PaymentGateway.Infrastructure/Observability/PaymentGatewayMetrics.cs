using System.Diagnostics.Metrics;

namespace PaymentGateway.Infrastructure.Observability;

/// <summary>
/// Application-level metrics exposed via .NET 9 Meter API. These are the same metric names listed
/// in the Phase 1 architecture spec. Consumed by the API metrics middleware and the workers.
///
/// Never log secrets or sensitive payment data. Metrics count events or record durations only.
/// </summary>
public sealed class PaymentGatewayMetrics : IDisposable
{
    public static readonly string MeterName = "PaymentGateway";
    private static readonly string MeterVersion = "1.0.0";

    private readonly Meter _meter;

    // Counters (monotonic increasing)
    public Counter<int> PaymentsCreated { get; }
    public Counter<int> PaymentsAuthorized { get; }
    public Counter<int> PaymentsSettled { get; }
    public Counter<int> PaymentsFailed { get; }
    public Counter<int> RefundsProcessed { get; }
    public Counter<int> LedgerPostings { get; }
    public Counter<int> WebhookDelivered { get; }
    public Counter<int> WebhookDeliveryFailures { get; }
    public Counter<int> OutboxProcessed { get; }
    public Counter<int> OutboxRetry { get; }
    public Counter<int> OutboxPoisoned { get; }
    public Counter<int> PaymentsRecovered { get; }
    public Counter<int> PaymentsForceFailed { get; }

    // Histograms (distributions)
    public Histogram<double> PaymentProcessingDuration { get; }
    public Histogram<int> OutboxPending { get; }
    public Histogram<int> ReconciliationAccountsChecked { get; }
    public Histogram<int> ReconciliationDiscrepancies { get; }

    public PaymentGatewayMetrics()
    {
        _meter = new Meter(MeterName, MeterVersion);

        PaymentsCreated = _meter.CreateCounter<int>("payments_created_total");
        PaymentsAuthorized = _meter.CreateCounter<int>("payments_authorized_total");
        PaymentsSettled = _meter.CreateCounter<int>("payments_settled_total");
        PaymentsFailed = _meter.CreateCounter<int>("payments_failed_total");
        RefundsProcessed = _meter.CreateCounter<int>("refunds_total");
        LedgerPostings = _meter.CreateCounter<int>("ledger_postings_total");
        WebhookDelivered = _meter.CreateCounter<int>("webhook_delivery_total");
        WebhookDeliveryFailures = _meter.CreateCounter<int>("webhook_delivery_failures_total");
        OutboxProcessed = _meter.CreateCounter<int>("outbox_processed_total");
        OutboxRetry = _meter.CreateCounter<int>("outbox_retry_total");
        OutboxPoisoned = _meter.CreateCounter<int>("outbox_poisoned_total");
        PaymentsRecovered = _meter.CreateCounter<int>("payments_recovered_total");
        PaymentsForceFailed = _meter.CreateCounter<int>("payments_force_failed_total");

        PaymentProcessingDuration = _meter.CreateHistogram<double>(
            "payment_processing_duration_seconds",
            unit: "s",
            description: "Time from payment creation to settlement.");
        OutboxPending = _meter.CreateHistogram<int>("outbox_pending_count");
        ReconciliationAccountsChecked = _meter.CreateHistogram<int>("reconciliation_accounts_checked");
        ReconciliationDiscrepancies = _meter.CreateHistogram<int>("reconciliation_discrepancies");
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
