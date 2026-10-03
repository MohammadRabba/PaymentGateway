using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PaymentGateway.Application.Common;
using PaymentGateway.Application.Idempotency;
using PaymentGateway.Application.Payments;
using PaymentGateway.Application.Risk;
using PaymentGateway.Application.Options;
using PaymentGateway.Application.Auditing;
using PaymentGateway.Application.Persistence;
using PaymentGateway.Domain.Entities;
using PaymentGateway.Domain.Enums;
using PaymentGateway.Infrastructure.Persistence;
using PaymentGateway.Infrastructure.Time;
using Xunit;

namespace PaymentGateway.UnitTests.Application;

public class CreatePaymentHandlerRiskGateTests
{
    private static (IPaymentGatewayDbContext Adapter, PaymentGatewayDbContext Inner) CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PaymentGatewayDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var inner = new PaymentGatewayDbContext(options);
        var adapter = new TestDbContextAdapter(inner);
        return (adapter, inner);
    }

    private static IdempotencyCheckResult.Proceed MakeProceed(IPaymentGatewayDbContext ctx, Guid merchantId, string idempotencyKey)
    {
        var record = new IdempotencyRecord(Guid.NewGuid(), merchantId, OperationType.Payment, idempotencyKey, "hash", DateTimeOffset.UtcNow, TimeSpan.FromHours(1));
        ctx.IdempotencyRecords.Add(record);
        ctx.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new IdempotencyCheckResult.Proceed(record.Id, "scope", record);
    }

    private sealed class NoopDistributedLock : IDistributedLock
    {
        public Task<IDistributedLockHandle?> AcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken) => Task.FromResult<IDistributedLockHandle?>(new NoopHandle(key));
        private sealed class NoopHandle : IDistributedLockHandle
        {
            public NoopHandle(string key) { Key = key; Token = Guid.NewGuid().ToString(); }
            public string Key { get; }
            public string Token { get; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task<bool> ReleaseAsync() => Task.FromResult(true);
        }
    }

    private sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IdempotencyStoreEntry> _store = new();
        public Task<IdempotencyStoreEntry?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult(_store.TryGetValue(key, out var e) ? e : null);
        public Task<bool> TrySetAsync(string key, IdempotencyStoreEntry entry, TimeSpan ttl, CancellationToken cancellationToken) => Task.FromResult(_store.TryAdd(key, entry));
        public Task SetAsync(string key, IdempotencyStoreEntry entry, TimeSpan ttl, CancellationToken cancellationToken) { _store[key] = entry; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken cancellationToken) { _store.TryRemove(key, out _); return Task.CompletedTask; }
    }

    // Adapter that forwards DbSet and SaveChanges calls to the inner PaymentGatewayDbContext
    // but provides a no-op transaction implementation so tests don't rely on relational DB features.
    private sealed class TestDbContextAdapter : IPaymentGatewayDbContext
    {
        private readonly PaymentGatewayDbContext _inner;
        public TestDbContextAdapter(PaymentGatewayDbContext inner) => _inner = inner;
        public DbSet<Merchant> Merchants => _inner.Merchants;
        public DbSet<Account> Accounts => _inner.Accounts;
        public DbSet<Payment> Payments => _inner.Payments;
        public DbSet<Refund> Refunds => _inner.Refunds;
        public DbSet<LedgerTransaction> LedgerTransactions => _inner.LedgerTransactions;
        public DbSet<LedgerEntry> LedgerEntries => _inner.LedgerEntries;
        public DbSet<OutboxMessage> OutboxMessages => _inner.OutboxMessages;
        public DbSet<OutboxDeadLetter> OutboxDeadLetters => _inner.OutboxDeadLetters;
        public DbSet<WebhookDelivery> WebhookDeliveries => _inner.WebhookDeliveries;
        public DbSet<AuditRecord> AuditRecords => _inner.AuditRecords;
        public DbSet<IdempotencyRecord> IdempotencyRecords => _inner.IdempotencyRecords;
        public DbSet<RiskAssessment> RiskAssessments => _inner.RiskAssessments;
        public DatabaseFacade Database => _inner.Database;
        public ChangeTracker ChangeTracker => _inner.ChangeTracker;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _inner.SaveChangesAsync(cancellationToken);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new NoopDbTransaction());
        public Task<IDbContextTransaction> BeginTransactionAsync(System.Data.IsolationLevel isolationLevel, CancellationToken cancellationToken = default)
            => Task.FromResult<IDbContextTransaction>(new NoopDbTransaction());

        private sealed class NoopDbTransaction : IDbContextTransaction
        {
            public Guid TransactionId => Guid.Empty;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public void Commit() { }
            public void Rollback() { }
        }
    }

    [Fact]
    public async Task Allow_calls_acquirer_and_persists_assessment()
    {
        var (ctx, inner) = CreateInMemoryContext("allow-db");
        try
        {
            var merchantId = Guid.NewGuid();
            var request = new PaymentGateway.Application.Contracts.CreatePaymentRequest
            {
                Amount = 100m,
                Currency = "USD",
                CardToken = "tok_123",
                IdempotencyKey = "k1",
                MerchantId = merchantId
            };

            var proceed = MakeProceed(ctx, merchantId, request.IdempotencyKey);

            var mockAcquirer = new Mock<IAcquirerClient>();
            mockAcquirer.Setup(a => a.AuthorizeAsync(It.IsAny<AcquirerAuthorizationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AcquirerAuthorization { AuthCode = "A1", AcquirerReference = "R1", Outcome = AcquirerOutcome.Success });

            var mockFraudClient = new Mock<IFraudDetectionClient>();
            mockFraudClient.Setup(f => f.ScoreAsync(It.IsAny<FraudScoreRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FraudRiskScore { Score = 0.01m, ModelVersion = "v1" });

            var options = Options.Create(new FraudOptions { Enabled = true, BlockThreshold = 0.96m, ReviewThreshold = 0.5m, Timeout = TimeSpan.FromMilliseconds(500), ServiceUrl = string.Empty });
            var idGen = new SequentialGuidGenerator();
            var clock = new SystemClock();
            var correlation = new CorrelationContext();
            var audit = new AuditService(ctx, idGen, clock, correlation);
            var riskGate = new RiskGate(ctx, mockFraudClient.Object, clock, idGen, options, audit, NullLogger<RiskGate>.Instance);

            var idempStore = new InMemoryIdempotencyStore();
            var idempOptions = Options.Create(new IdempotencyOptions());
            var idempotencyService = new IdempotencyService(ctx, idempStore, clock, idGen, idempOptions, NullLogger<IdempotencyService>.Instance);

            var handler = new CreatePaymentHandler(ctx, mockAcquirer.Object, new NoopDistributedLock(), idempotencyService, new RequestFingerprinter(), clock, idGen, correlation, audit, NullLogger<CreatePaymentHandler>.Instance, riskGate);

            // Invoke private ExecuteCreateAsync via reflection
            var method = typeof(CreatePaymentHandler).GetMethod("ExecuteCreateAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var task = (Task<PaymentGateway.Application.Contracts.CreatePaymentResponse>)method.Invoke(handler, new object[] { request, proceed, CancellationToken.None })!;
            var response = await task;

            // Verify acquirer was called
            mockAcquirer.Verify(a => a.AuthorizeAsync(It.IsAny<AcquirerAuthorizationRequest>(), It.IsAny<CancellationToken>()), Times.Once);

            // Verify payment recorded as Authorized and outbox message exists
            var payment = inner.Payments.FirstOrDefault(p => p.Id == response.PaymentId);
            payment.Should().NotBeNull();
            payment!.Status.Should().Be(PaymentStatus.Authorized);

            inner.OutboxMessages.Any(o => o.EventType == "PaymentAuthorizedEvent").Should().BeTrue();

            // Risk assessment persisted
            inner.RiskAssessments.Any(r => r.PaymentId == response.PaymentId).Should().BeTrue();
        }
        finally
        {
            inner.Dispose();
        }
    }

    [Fact]
    public async Task Block_does_not_call_acquirer_and_marks_failed()
    {
        var (ctx, inner) = CreateInMemoryContext("block-db");
        try
        {
            var merchantId = Guid.NewGuid();
            var request = new PaymentGateway.Application.Contracts.CreatePaymentRequest
            {
                Amount = 100m,
                Currency = "USD",
                CardToken = "tok_123",
                IdempotencyKey = "k-block",
                MerchantId = merchantId
            };

            var proceed = MakeProceed(ctx, merchantId, request.IdempotencyKey);

            var mockAcquirer = new Mock<IAcquirerClient>();

            var mockFraudClient = new Mock<IFraudDetectionClient>();
            mockFraudClient.Setup(f => f.ScoreAsync(It.IsAny<FraudScoreRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FraudRiskScore { Score = 0.99m, ModelVersion = "v1" });

            var options = Options.Create(new FraudOptions { Enabled = true, BlockThreshold = 0.96m, ReviewThreshold = 0.5m, Timeout = TimeSpan.FromMilliseconds(500), ServiceUrl = string.Empty });
            var idGen = new SequentialGuidGenerator();
            var clock = new SystemClock();
            var correlation = new CorrelationContext();
            var audit = new AuditService(ctx, idGen, clock, correlation);
            var riskGate = new RiskGate(ctx, mockFraudClient.Object, clock, idGen, options, audit, NullLogger<RiskGate>.Instance);

            var idempStore = new InMemoryIdempotencyStore();
            var idempOptions = Options.Create(new IdempotencyOptions());
            var idempotencyService = new IdempotencyService(ctx, idempStore, clock, idGen, idempOptions, NullLogger<IdempotencyService>.Instance);

            var handler = new CreatePaymentHandler(ctx, mockAcquirer.Object, new NoopDistributedLock(), idempotencyService, new RequestFingerprinter(), clock, idGen, correlation, audit, NullLogger<CreatePaymentHandler>.Instance, riskGate);

            var method = typeof(CreatePaymentHandler).GetMethod("ExecuteCreateAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var task = (Task<PaymentGateway.Application.Contracts.CreatePaymentResponse>)method.Invoke(handler, new object[] { request, proceed, CancellationToken.None })!;
            var response = await task;

            // Acquirer must NOT have been called
            mockAcquirer.Verify(a => a.AuthorizeAsync(It.IsAny<AcquirerAuthorizationRequest>(), It.IsAny<CancellationToken>()), Times.Never);

            var payment = inner.Payments.FirstOrDefault(p => p.Id == response.PaymentId);
            payment.Should().NotBeNull();
            payment!.Status.Should().Be(PaymentStatus.Failed);

            inner.OutboxMessages.Any(o => o.EventType == "PaymentBlockedAsFraudEvent").Should().BeTrue();

            // Idempotency record completed with 403
            var rec = inner.IdempotencyRecords.First(r => r.Id == proceed.IdempotencyRecordId);
            rec.StatusCode.Should().Be(403);
            rec.State.Should().Be(IdempotencyState.Completed);
        }
        finally
        {
            inner.Dispose();
        }
    }

    [Fact]
    public async Task Failure_following_failclosed_block_behaves_as_block()
    {
        var (ctx, inner) = CreateInMemoryContext("fail-db");
        try
        {
            var merchantId = Guid.NewGuid();
            var request = new PaymentGateway.Application.Contracts.CreatePaymentRequest
            {
                Amount = 100m,
                Currency = "USD",
                CardToken = "tok_123",
                IdempotencyKey = "k-fail",
                MerchantId = merchantId
            };

            var proceed = MakeProceed(ctx, merchantId, request.IdempotencyKey);

            var mockAcquirer = new Mock<IAcquirerClient>();

            var mockFraudClient = new Mock<IFraudDetectionClient>();
            mockFraudClient.Setup(f => f.ScoreAsync(It.IsAny<FraudScoreRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("down"));

            var options = Options.Create(new FraudOptions { Enabled = true, BlockThreshold = 0.96m, ReviewThreshold = 0.5m, Timeout = TimeSpan.FromMilliseconds(10), ServiceUrl = string.Empty, FailClosedMode = FailClosedMode.Block });
            var idGen = new SequentialGuidGenerator();
            var clock = new SystemClock();
            var correlation = new CorrelationContext();
            var audit = new AuditService(ctx, idGen, clock, correlation);
            var riskGate = new RiskGate(ctx, mockFraudClient.Object, clock, idGen, options, audit, NullLogger<RiskGate>.Instance);

            var idempStore = new InMemoryIdempotencyStore();
            var idempOptions = Options.Create(new IdempotencyOptions());
            var idempotencyService = new IdempotencyService(ctx, idempStore, clock, idGen, idempOptions, NullLogger<IdempotencyService>.Instance);

            var handler = new CreatePaymentHandler(ctx, mockAcquirer.Object, new NoopDistributedLock(), idempotencyService, new RequestFingerprinter(), clock, idGen, correlation, audit, NullLogger<CreatePaymentHandler>.Instance, riskGate);

            var method = typeof(CreatePaymentHandler).GetMethod("ExecuteCreateAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var task = (Task<PaymentGateway.Application.Contracts.CreatePaymentResponse>)method.Invoke(handler, new object[] { request, proceed, CancellationToken.None })!;
            var response = await task;

            // Acquirer must NOT have been called because FailClosedMode=Block
            mockAcquirer.Verify(a => a.AuthorizeAsync(It.IsAny<AcquirerAuthorizationRequest>(), It.IsAny<CancellationToken>()), Times.Never);

            var payment = inner.Payments.FirstOrDefault(p => p.Id == response.PaymentId);
            payment.Should().NotBeNull();
            payment!.Status.Should().Be(PaymentStatus.Failed);

            inner.OutboxMessages.Any(o => o.EventType == "PaymentBlockedAsFraudEvent").Should().BeTrue();
        }
        finally
        {
            inner.Dispose();
        }
    }

    [Fact]
    public async Task Disabled_skips_risk_gate_and_calls_acquirer()
    {
        var (ctx, inner) = CreateInMemoryContext("disabled-db");
        try
        {
            var merchantId = Guid.NewGuid();
            var request = new PaymentGateway.Application.Contracts.CreatePaymentRequest
            {
                Amount = 100m,
                Currency = "USD",
                CardToken = "tok_123",
                IdempotencyKey = "k-disabled",
                MerchantId = merchantId
            };

            var proceed = MakeProceed(ctx, merchantId, request.IdempotencyKey);

            var mockAcquirer = new Mock<IAcquirerClient>();
            mockAcquirer.Setup(a => a.AuthorizeAsync(It.IsAny<AcquirerAuthorizationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AcquirerAuthorization { AuthCode = "A1", AcquirerReference = "R1", Outcome = AcquirerOutcome.Success });

            var mockFraudClient = new Mock<IFraudDetectionClient>();

            var options = Options.Create(new FraudOptions { Enabled = false, BlockThreshold = 0.96m, ReviewThreshold = 0.5m, Timeout = TimeSpan.FromMilliseconds(500), ServiceUrl = string.Empty });
            var idGen = new SequentialGuidGenerator();
            var clock = new SystemClock();
            var correlation = new CorrelationContext();
            var audit = new AuditService(ctx, idGen, clock, correlation);
            var riskGate = new RiskGate(ctx, mockFraudClient.Object, clock, idGen, options, audit, NullLogger<RiskGate>.Instance);

            var idempStore = new InMemoryIdempotencyStore();
            var idempOptions = Options.Create(new IdempotencyOptions());
            var idempotencyService = new IdempotencyService(ctx, idempStore, clock, idGen, idempOptions, NullLogger<IdempotencyService>.Instance);

            var handler = new CreatePaymentHandler(ctx, mockAcquirer.Object, new NoopDistributedLock(), idempotencyService, new RequestFingerprinter(), clock, idGen, correlation, audit, NullLogger<CreatePaymentHandler>.Instance, riskGate);

            var method = typeof(CreatePaymentHandler).GetMethod("ExecuteCreateAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var task = (Task<PaymentGateway.Application.Contracts.CreatePaymentResponse>)method.Invoke(handler, new object[] { request, proceed, CancellationToken.None })!;
            var response = await task;

            mockFraudClient.Verify(f => f.ScoreAsync(It.IsAny<FraudScoreRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            mockAcquirer.Verify(a => a.AuthorizeAsync(It.IsAny<AcquirerAuthorizationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            inner.Dispose();
        }
    }

}
