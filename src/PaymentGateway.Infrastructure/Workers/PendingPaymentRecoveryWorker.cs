using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Observability;

namespace PaymentGateway.Infrastructure.Workers;

/// <summary>
/// Background worker for stuck payments. Detects payments in Processing or Unknown state whose
/// HeartbeatAt is older than StuckPaymentThreshold, and resolves them safely by querying the
/// acquirer — NEVER by re-issuing an authorization.
///
/// Scoping: the worker is a singleton IHostedService. Each scan iteration creates a fresh DI scope
/// via IServiceScopeFactory so the RecoverPaymentHandler and DbContext are not captured as
/// singletons.
/// </summary>
public sealed class PendingPaymentRecoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly WorkerOptions _options;
    private readonly PaymentGatewayMetrics _metrics;
    private readonly ILogger<PendingPaymentRecoveryWorker> _logger;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _recoveryAttempts = new();

    public PendingPaymentRecoveryWorker(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        IOptions<WorkerOptions> options,
        PaymentGatewayMetrics metrics,
        ILogger<PendingPaymentRecoveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PendingPaymentRecoveryWorker started.");

        await Task.Delay(_options.StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<IPaymentGatewayDbContext>();
                var recoverHandler = scope.ServiceProvider.GetRequiredService<RecoverPaymentHandler>();
                await ScanAndRecoverAsync(dbContext, recoverHandler, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PendingPaymentRecoveryWorker iteration failed.");
            }

            try
            {
                await Task.Delay(_options.RecoveryPollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PendingPaymentRecoveryWorker stopped.");
    }

    private async Task ScanAndRecoverAsync(IPaymentGatewayDbContext dbContext, RecoverPaymentHandler recoverHandler, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var threshold = now.Subtract(_options.StuckPaymentThreshold);

        var stuckPayments = await dbContext.Payments
            .AsNoTracking()
            .Where(p => (p.Status == PaymentStatus.Processing || p.Status == PaymentStatus.Unknown)
                     && (p.HeartbeatAt == null || p.HeartbeatAt < threshold))
            .Select(p => new { p.Id, p.MerchantId, p.Status, p.CreatedAt, p.HeartbeatAt })
            .ToListAsync(cancellationToken);

        if (stuckPayments.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} stuck payments to recover.", stuckPayments.Count);

        foreach (var payment in stuckPayments)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await RecoverOneAsync(recoverHandler, payment.Id, payment.MerchantId, cancellationToken);
        }
    }

    private async Task RecoverOneAsync(RecoverPaymentHandler recoverHandler, Guid paymentId, Guid merchantId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Recovering payment {PaymentId}.", paymentId);

            var response = await recoverHandler.HandleAsync(paymentId, merchantId, cancellationToken);
            _metrics.PaymentsRecovered.Add(1);

            _logger.LogInformation("Payment {PaymentId} recovered to status {Status}.", paymentId, response.Status);
            _recoveryAttempts.TryRemove(paymentId, out _);
        }
        catch (PaymentGateway.Domain.Exceptions.UnknownAcquirerOutcomeException)
        {
            var attempts = _recoveryAttempts.AddOrUpdate(paymentId, 1, (_, v) => v + 1);
            _logger.LogInformation("Payment {PaymentId} still Unknown after recovery attempt {Attempt}.", paymentId, attempts);

            if (attempts >= _options.MaxRecoveryAttempts)
            {
                await ForceFailAsync(recoverHandler, paymentId, merchantId, cancellationToken);
                _recoveryAttempts.TryRemove(paymentId, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery of payment {PaymentId} failed.", paymentId);
        }
    }

    private async Task ForceFailAsync(RecoverPaymentHandler recoverHandler, Guid paymentId, Guid merchantId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning("Force-failing payment {PaymentId} after {MaxAttempts} recovery attempts.", paymentId, _options.MaxRecoveryAttempts);

            await recoverHandler.ForceFailAsync(
                paymentId,
                merchantId,
                "RecoveryTimeout: acquirer outcome remained unknown after maximum recovery attempts.",
                cancellationToken);

            _metrics.PaymentsForceFailed.Add(1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Force-fail of payment {PaymentId} failed.", paymentId);
        }
    }
}
