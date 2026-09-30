using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace PaymentGateway.Infrastructure.Messaging;

/// <summary>
/// Wraps the RabbitMQ ConnectionFactory. The connection is automatically reconnecting (via
/// RabbitMQ.Client's built-in auto-recovery). Singleton-scoped: shared across the OutboxPublisherWorker
/// and the WebhookEventConsumer. Disposed on application shutdown.
/// </summary>
public sealed class RabbitMqConnection : IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnection> _logger;
    private IConnection? _connection;
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    public RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Get a new channel from the (lazy-initialised) connection. The caller owns the channel and
    /// is responsible for disposing it.
    /// </summary>
    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        var conn = await GetConnectionAsync(cancellationToken);
        return await conn.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Ensure the connection is established. Thread-safe via a semaphore. If connection fails,
    /// throws — the caller (worker) should catch and retry on the next iteration.
    /// </summary>
    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } openConn)
        {
            return openConn;
        }

        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } existingConn)
            {
                return existingConn;
            }

            if (_connection is not null)
            {
                try { await _connection.DisposeAsync(); } catch { /* ignore */ }
                _connection = null;
            }

            var factory = new ConnectionFactory
            {
                Uri = new Uri(_options.ConnectionUri),
                // DispatchConsumersAsync = false, // Use synchronous consumer for simpler control flow.
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = _options.InitialReconnectDelay,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _logger.LogInformation("Connected to RabbitMQ at {Uri}", _options.ConnectionUri);
            return _connection;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing RabbitMQ connection.");
            }
        }

        _connectGate.Dispose();
    }
}
