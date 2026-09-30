# Open Payment Gateway & Settlement Engine

A production-grade .NET 9 payment gateway and settlement engine implementing strict double-entry accounting, transactional outbox event publishing, idempotent APIs, distributed coordination, reliable webhook delivery, and crash-safe recovery.

This is a financial systems engineering project, not a CRUD demonstration. The design optimises for **correctness → consistency → recoverability → observability → security → maintainability → performance**. When requirements conflict, the design preserves financial integrity.

---

## Architecture Overview

```
┌────────────────────────────────────────────────────────────┐
│                     PaymentGateway.Api                      │
│  (endpoints, middleware, auth, ProblemDetails, health, DI) │
└──────┬───────────────────────┬─────────────────────────────┘
       │                       │
       ▼                       ▼
┌──────────────┐     ┌──────────────────────────┐
│  Application │     │     Infrastructure        │
│  (handlers,  │     │  (EF Core, Redis,         │
│   DTOs,      │     │   RabbitMQ, Polly,        │
│   services)  │     │   acquirer, workers,      │
└──────┬───────┘     │   observability)          │
       │             └──────────┬───────────────┘
       │                        │
       ▼                        ▼
    ┌─────────────────────────────┐
    │         Domain              │
    │  (entities, VOs,            │
    │   state machines,           │
    │   ledger rules)             │
    └─────────────────────────────┘
```

### Project Structure

```
payment-gateway/
├── src/
│   ├── PaymentGateway.Domain/         # Pure C# — no infrastructure deps
│   ├── PaymentGateway.Application/    # Use cases + EF Core abstractions
│   ├── PaymentGateway.Infrastructure/ # EF Core, Redis, RabbitMQ, Polly, workers
│   └── PaymentGateway.Api/            # ASP.NET Core, middleware, endpoints
├── tests/
│   ├── PaymentGateway.UnitTests/
│   ├── PaymentGateway.IntegrationTests/
│   └── PaymentGateway.ConcurrencyTests/
├── deploy/docker/                     # Dockerfiles
├── docker-compose.yml
└── README.md (this file)
```

---

## Key Design Decisions

### SQL Server is the authoritative ledger

All financial mutations happen in serializable transactions with `UPDLOCK, HOLDLOCK` hints via `FromSqlInterpolated`. Redis is coordination only (idempotency cache, distributed locks). If Redis is unavailable, the system degrades gracefully to SQL-only mode — slower, still correct.

### Authorisation ≠ Settlement

`CreatePaymentHandler` transitions to `Authorized` (acquirer approved) but does NOT post the ledger. `SettlePaymentHandler` is a separate endpoint that posts the double-entry ledger atomically. This separation makes authorisation and settlement independently observable, recoverable, and idempotent.

### Unknown acquirer outcomes are first-class

When the acquirer times out or the outcome is unknown, the payment transitions to `Unknown`. The `PendingPaymentRecoveryWorker` queries the acquirer by the deterministic idempotency key (`PAY-{paymentId}`) — it NEVER re-issues an authorization. The simulator tracks authorizations by key, so the recovery worker gets the original outcome.

### Idempotency is multi-layered

1. Redis fast path (`SET NX PX`)
2. SQL `UNIQUE(MerchantId, Operation, IdempotencyKey)` constraint
3. Payment state machine (already-settled = no-op)

Each layer catches duplicates the previous might miss.

### Transactional outbox

Every financial mutation includes the `OutboxMessage` in the same EF `SaveChanges`/transaction commit. The `OutboxPublisherWorker` drains the outbox into RabbitMQ with at-least-once semantics. Consumers dedup by `EventId`. Poisoned messages move to `OutboxDeadLetter` (never deleted).

---

## Prerequisites

- .NET 9 SDK
- Docker (with Docker Compose)
- (Optional) `dotnet ef` tool for migrations: `dotnet tool install --global dotnet-ef`

---

## Local Setup

### Option 1: Docker Compose (recommended)

```bash
# 1. Copy the env template
cp .env.example .env

# 2. Generate an admin API key hash
echo -n 'my-admin-key' | sha256sum
# Paste the hex output into .env: ADMIN_API_KEY_HASH=<hex>

# 3. Start the stack
docker compose up -d

# 4. Check health
curl http://localhost:8080/health/ready
```

### Option 2: Run API locally with Docker dependencies

```bash
# Start only the dependencies
docker compose up -d sqlserver redis rabbitmq

# Run the API
dotnet run --project src/PaymentGateway.Api
```

The API applies EF migrations automatically on startup. If migrations fail, the API refuses to start — a payment gateway must not run against an inconsistent schema.

---

## Database Migrations

The API applies migrations automatically on startup via `DbInitializer`. For manual control:

```bash
# Generate a new migration
dotnet ef migrations add YourMigrationName \
  --project src/PaymentGateway.Infrastructure \
  --startup-project src/PaymentGateway.Api

# Apply migrations manually
dotnet ef database update \
  --project src/PaymentGateway.Infrastructure \
  --startup-project src/PaymentGateway.Api

# Or use the migrations Dockerfile
docker compose -f docker-compose.yml run --rm migrations
```

---

## Configuration

All configuration is via strongly-typed options in `appsettings.json` or environment variables. Required configuration:

| Setting | Required | Description |
|---------|---------|-------------|
| `ConnectionStrings:PaymentGateway` | Yes | SQL Server connection string |
| `Redis:ConnectionString` | Yes | Redis connection string |
| `RabbitMq:ConnectionUri` | Yes | RabbitMQ AMQP URI |
| `Security:AdminApiKeyHash` | Production | SHA-256 hash of admin API key |
| `Security:RequireAdminKey` | Production | Set to `true` in production |
| `Acquirer:FailureMode` | No | `None` (default), `Decline`, `Transient5xx`, `Timeout`, `Unknown`, `Random` |
| `Fees:CalculateFeesEnabled` | No | Default `false`. If true, settlement posts 3-entry ledger with fees. |
| `Fees:FeePercent` | No | E.g., `2.5` for 2.5% fee. |

---

## API Examples

### Register a Merchant (admin)

```bash
curl -X POST http://localhost:8080/api/v1/merchants \
  -H "X-Admin-Key: my-admin-key" \
  -H "X-Idempotency-Key: merchant-001" \
  -H "Content-Type: application/json" \
  -d '{
    "externalReference": "merchant-001",
    "name": "Acme Corp",
    "webhookUrl": "https://example.com/webhook"
  }'
```

Response (201 Created):
```json
{
  "id": "guid-here",
  "externalReference": "merchant-001",
  "name": "Acme Corp",
  "webhookUrl": "https://example.com/webhook",
  "status": "Active",
  "createdAt": "2026-01-01T00:00:00Z",
  "apiKey": "pgk_xxxxxxxx..."
}
```

**The `apiKey` is returned only once.** Store it securely — it cannot be retrieved again.

### Create a Payment

```bash
curl -X POST http://localhost:8080/api/v1/payments \
  -H "Authorization: Bearer pgk_xxxxxxxx..." \
  -H "X-Idempotency-Key: payment-001" \
  -H "Content-Type: application/json" \
  -d '{
    "amount": 100.00,
    "currency": "USD",
    "cardToken": "tok_test_card"
  }'
```

Response (201 Created):
```json
{
  "paymentId": "guid-here",
  "status": "Authorized",
  "amount": 100.00,
  "currency": "USD",
  "createdAt": "2026-01-01T00:00:00Z"
}
```

### Settle a Payment

```bash
curl -X POST http://localhost:8080/api/v1/payments/{paymentId}/settle \
  -H "Authorization: Bearer pgk_xxxxxxxx..." \
  -H "X-Idempotency-Key: settle-001"
```

### Refund a Payment

```bash
curl -X POST http://localhost:8080/api/v1/payments/{paymentId}/refunds \
  -H "Authorization: Bearer pgk_xxxxxxxx..." \
  -H "X-Idempotency-Key: refund-001" \
  -H "Content-Type: application/json" \
  -d '{
    "amount": 50.00,
    "currency": "USD",
    "reason": "Customer request"
  }'
```

### Get Payment

```bash
curl http://localhost:8080/api/v1/payments/{paymentId} \
  -H "Authorization: Bearer pgk_xxxxxxxx..."
```

### Get Account Balance

```bash
curl http://localhost:8080/api/v1/accounts \
  -H "Authorization: Bearer pgk_xxxxxxxx..."
```

### Run Reconciliation (admin)

```bash
curl -X POST http://localhost:8080/api/v1/admin/reconcile \
  -H "X-Admin-Key: my-admin-key"
```

---

## Idempotency Behaviour

Every mutating payment/refund endpoint requires the `X-Idempotency-Key` header.

| Scenario | Response |
|----------|----------|
| First request | Process and return result (201 or 200) |
| Concurrent duplicate (Processing) | 409 Conflict + `Retry-After: 2` |
| Completed duplicate (same payload) | Replay the cached response |
| Same key + different payload | 422 `idempotency-key-reuse` |
| Missing header | 400 ProblemDetails |

Idempotency keys are scoped by `(MerchantId, OperationType, Key)` so a payment key never collides with a refund key.

---

## Ledger Model

Double-entry accounting with hard invariants:

- `Σ Debits == Σ Credits` per ledger transaction (enforced inside the transaction)
- All entries within a transaction share the same currency
- Ledger entries are **append-only** — no update, no delete in business paths
- Corrections are new `LedgerTransaction` of type `Correction`

**Posting patterns:**

Payment settlement (no fees):
| Account | Entry | Amount |
|---------|-------|--------|
| AcquirerReceivable | Dr | gross |
| MerchantPayable | Cr | gross |

Payment settlement (with fees):
| Account | Entry | Amount |
|---------|-------|--------|
| AcquirerReceivable | Dr | gross |
| MerchantPayable | Cr | net (= gross − fee) |
| FeeRevenue | Cr | fee |

Refund:
| Account | Entry | Amount |
|---------|-------|--------|
| MerchantPayable | Dr | refund amount |
| AcquirerReceivable | Cr | refund amount |

`Account.Balance` is a materialised view; the ledger is authoritative. The `ReconciliationService` verifies `Balance == Σ LedgerEntries` and reports discrepancies (never auto-repairs).

---

## Outbox Architecture

```
Payment mutation (SQL transaction)
  ├─ LedgerEntries
  ├─ Account.Balance update
  ├─ Payment.Status update
  ├─ OutboxMessage insert
  └─ AuditRecord insert
  COMMIT
        │
        ▼
OutboxPublisherWorker (background)
  ├─ Claim messages atomically
  ├─ Publish to RabbitMQ (persistent, durable exchange)
  ├─ Mark Published on success
  ├─ Retry with exponential backoff on transient failure
  └─ Poison after MaxAttempts → OutboxDeadLetter (never deleted)
        │
        ▼
RabbitMQ (durable exchange + queue + DLX)
        │
        ▼
WebhookEventConsumer
  ├─ Consumer-side idempotency by EventId
  ├─ Look up merchant webhook URL + secret
  ├─ HMAC-SHA256 sign + POST
  └─ ACK on 2xx/permanent 4xx; NACK+DLQ on retryable
        │
        ▼
Merchant
```

**At-least-once delivery.** Consumers must dedup. A crash between HTTP delivery and SQL commit can result in duplicate HTTP delivery — merchants MUST use `X-Webhook-Id` as their idempotency key.

---

## Webhook Architecture

Webhook payloads are signed with HMAC-SHA256:

```
signature = lowercase_hex(HMAC_SHA256(secret, "{WebhookId}.{UnixTimestamp}.{RawJsonPayload}"))
```

Headers sent with every webhook:

| Header | Description |
|--------|-------------|
| `X-Webhook-Id` | Unique webhook ID. Merchants use this as idempotency key. |
| `X-Webhook-Timestamp` | Unix timestamp (seconds) |
| `X-Webhook-Signature` | Lowercase-hex HMAC-SHA256 signature |
| `X-Webhook-Event` | Event type (e.g., `payment.settled`) |

Replay protection: reject if `|now − timestamp| > Tolerance` (default 300s).

Retry classification:
| HTTP status | Behaviour |
|------------|----------|
| 2xx | Success, ACK |
| 408, 429, 5xx, network/timeout | Retry with exponential backoff |
| 400, 401, 403, 404, 410, 422 | Permanent failure → DLQ after MaxAttempts |
| Other 4xx | Retryable, then DLQ |

---

## Testing

### Run all tests

```bash
dotnet test --configuration Release
```

### Unit tests

```bash
dotnet test tests/PaymentGateway.UnitTests --configuration Release
```

Covers: payment state machine, refund state machine, ledger balancing, refund rules, money arithmetic, currency validation, webhook signing, request fingerprinting, acquirer simulator, payment entity behavior.

### Integration tests

```bash
dotnet test tests/PaymentGateway.IntegrationTests --configuration Release
```

Uses Testcontainers to spin up real SQL Server, Redis, and RabbitMQ containers. Tests:
- Payment creation and persistence
- Idempotency: duplicate request replay, key-reuse detection
- Ledger balancing and immutability
- Reconciliation (corrupted balance detection)
- Settlement idempotency

### Concurrency tests

```bash
dotnet test tests/PaymentGateway.ConcurrencyTests --configuration Release
```

Uses `WebApplicationFactory` with Testcontainers. Tests:
- Concurrent duplicate payment requests (SQL unique constraint enforcement)
- Concurrent different-key requests (separate payments)
- Concurrent refund invariants

---

## Failure / Retry Semantics

| Scenario | Behaviour | Recovery |
|----------|----------|----------|
| DB unavailable | Ready check fails; mutating endpoints 503 | Reconnect; outbox resumes |
| Redis unavailable | Idempotency falls back to SQL; no corruption | Redis reconnect |
| RabbitMQ unavailable | Outbox accumulates; payments still succeed; webhooks delayed | RabbitMQ reconnect; outbox drains |
| Acquirer timeout | `Processing → Unknown`; recovery queries | Worker resolves later |
| Acquirer decline | `Processing → Failed` immediately | New payment |
| Concurrent duplicate | Redis NX + SQL unique → first wins; others 409 | Client retry |
| Same key + different payload | 422 `idempotency-key-reuse` | New key required |
| Optimistic concurrency conflict | rowversion → 3x retry then 409 | Client retry |
| Webhook timeout | Retry with backoff | Up to MaxAttempts |
| Webhook permanent 4xx | DLQ | Manual inspection |
| Consumer crash before ACK | RabbitMQ redelivers; consumer dedup by EventId | Automatic |
| Stuck Processing payment | Recovery worker queries acquirer after heartbeat timeout | No re-authorization |
| Recovery exhausted | Force-fail with reason "RecoveryTimeout" | Manual intervention |

---

## Known Limitations

- The acquirer is a simulator. Real acquirer integration requires implementing `IAcquirerClient` with a real HTTP client and proper card tokenisation.
- Card tokens are simulator-only references. The system never stores or logs real card data.
- Refunds are processed against the same acquirer reference. Real acquirer refund APIs may differ.
- No currency conversion. Each ledger transaction is single-currency.
- No partial settlement. A payment settles in full or fails.
- Health checks verify dependency connectivity, not full functionality.

---

## Production Hardening Recommendations

1. **Set `Security:RequireAdminKey=true`** and generate a strong admin key hash.
2. **Use managed secrets** (Azure Key Vault, AWS Secrets Manager) instead of env vars for production.
3. **Add TLS termination** in front of the API (nginx, traefik, or a cloud load balancer).
4. **Configure OTLP export** to a real collector ( Tempo, Prometheus, Loki).
5. **Configure Seq or Elasticsearch** for log aggregation.
6. **Set up alerting** on: `payments_failed_total`, `outbox_pending_count`, `webhook_delivery_failures_total`, `reconciliation_discrepancies`.
7. **Run the reconciliation worker** more frequently in production (e.g., every 15 minutes) to catch drift early.
8. **Implement a real acquirer client** with proper card tokenisation and PCI-DSS compliance.
9. **Add rate limiting** per merchant to prevent abuse.
10. **Add request signing** for high-value operations if your acquirer requires it.
11. **Monitor the OutboxDeadLetter table** and alert on new entries.
12. **Run multiple API replicas** behind a load balancer. The workers use `IServiceScopeFactory` and atomic claiming so they scale horizontally.

---

## License

Internal. Not for redistribution.
#   P a y m e n t G a t e w a y  
 #   P a y m e n t G a t e w a  
 