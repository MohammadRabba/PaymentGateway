using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Ledger;
using PaymentGateway.Infrastructure.Observability;

namespace PaymentGateway.Infrastructure.Workers;

/// <summary>
/// Background worker that periodically reconciles Account.Balance (materialised) against the
/// ledger (authoritative). Reports discrepancies via metrics and audit records — NEVER auto-repairs.
///
/// Scoping: the worker is a singleton IHostedService. Each reconciliation iteration creates a
/// fresh DI scope so the ReconciliationService and its DbContext dependency are not captured as
/// singletons.
/// </summary>
public sealed class ReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkerOptions _options;
    private readonly PaymentGatewayMetrics _metrics;
    private readonly ILogger<ReconciliationWorker> _logger;

    public ReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> options,
        PaymentGatewayMetrics metrics,
        ILogger<ReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReconciliationWorker started. Interval={Interval}", _options.ReconciliationInterval);

        await Task.Delay(_options.StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var reconciliationService = scope.ServiceProvider.GetRequiredService<ReconciliationService>();
                var result = await reconciliationService.ReconcileAllAsync(stoppingToken);

                _metrics.ReconciliationAccountsChecked.Record(result.AccountsChecked);
                _metrics.ReconciliationDiscrepancies.Record(result.DiscrepanciesFound);

                if (result.DiscrepanciesFound > 0)
                {
                    _logger.LogWarning("Reconciliation found {Count} discrepancies out of {Total} accounts.",
                        result.DiscrepanciesFound, result.AccountsChecked);
                }
                else
                {
                    _logger.LogInformation("Reconciliation passed: {Total} accounts consistent.", result.AccountsChecked);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReconciliationWorker iteration failed.");
            }

            try
            {
                await Task.Delay(_options.ReconciliationInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("ReconciliationWorker stopped.");
    }
}
