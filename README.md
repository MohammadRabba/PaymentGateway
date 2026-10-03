# Open Payment Gateway & Settlement Engine

A production-oriented .NET 9 payment gateway and settlement engine designed around correctness, consistency, recoverability, observability, security, and maintainability.

This project is not a CRUD demo. It models the realities of financial systems: strict double-entry accounting, transactional outbox publishing, idempotent APIs, distributed coordination, reliable webhook delivery, crash-safe recovery, and financial reconciliation.

## Why this project exists

The goal is to build a payment platform that behaves like a real financial system rather than a toy API. The implementation emphasizes:

- Financial correctness
- Consistency under concurrency
- Recoverability and safe retries
- Observability into runtime health and failures
- Security-minded configuration and API protection
- Maintainable architecture with explicit boundaries

---

## Architecture overview

```text
┌────────────────────────────────────────────────────────────┐
│                   PaymentGateway.Api                        │
│  HTTP endpoints, middleware, auth, ProblemDetails, health  │
└───────────────┬───────────────────────┬────────────────────┘
                │                       │
                ▼                       ▼
   ┌───────────────────┐     ┌──────────────────────────┐
   │   Application     │     │     Infrastructure       │
   │ handlers, DTOs,   │     │ EF Core, Redis, RabbitMQ,│
   │ services, use     │     │ Polly, workers, acquirer │
   │ cases             │     │ observability            │
   └─────────┬─────────┘     └────────────┬─────────────┘
             │                          │
             ▼                          ▼
      ┌──────────────────────┐
      │       Domain         │
      │ entities, VOs,       │
      │ state machines,      │
      │ ledger rules         │
      └──────────────────────┘
```

### Project structure

```text
payment-gateway/
├── src/
│   ├── PaymentGateway.Domain/         # Pure C# — no infrastructure dependencies
│   ├── PaymentGateway.Application/    # Use cases, handlers, abstractions
│   ├── PaymentGateway.Infrastructure/ # EF Core, Redis, RabbitMQ, Polly, workers
│   └── PaymentGateway.Api/            # ASP.NET Core app, middleware, endpoints
├── tests/
│   ├── PaymentGateway.UnitTests/
│   ├── PaymentGateway.IntegrationTests/
│   └── PaymentGateway.ConcurrencyTests/
├── deploy/docker/                    # Dockerfiles
├── docker-compose.yml
├── .env.example
├── README.md
└── LICENSE
```

---

## Key design decisions

### SQL Server is the authoritative ledger

All financial mutations run inside serializable transactions using `UPDLOCK, HOLDLOCK` hints through `FromSqlInterpolated`. Redis is used only for coordination, not as the system of record.

This means:

- financial correctness is protected by database transactions
- Redis provides idempotency and lock coordination
- ledger truth remains in SQL Server

### Authorisation is not the same as settlement

The payment flow is intentionally split:

- `CreatePaymentHandler` transitions a payment to `Authorized` once the acquirer approves it
- `SettlePaymentHandler` is a separate step that posts the double-entry ledger atomically

This keeps approval separate from final accounting.

### Unknown acquirer outcomes are first-class

If the acquirer times out or returns an unknown outcome, the payment transitions to `Unknown`. A recovery worker later queries the acquirer using a deterministic idempotency key to resolve the state.

### Idempotency is layered

Each mutating operation includes multiple levels of protection:

1. Redis fast path (`SET NX PX`)
2. SQL unique constraint on `(MerchantId, Operation, IdempotencyKey)`
3. Payment state checks to prevent reprocessing already-settled state

This helps guard against duplicate processing and operation collisions.

### Transactional outbox

Every financial mutation writes an `OutboxMessage` in the same EF `SaveChanges` transaction as the ledger and account updates. A background worker drains the outbox to RabbitMQ with at-least-once semantics.

This prevents events from being lost between database commit and downstream delivery.

---

## Prerequisites

- .NET 9 SDK
- Docker + Docker Compose
- Optional: `dotnet-ef` for manual migration work

Install the EF CLI if needed:

```bash
dotnet tool install --global dotnet-ef
```

---

## Local setup

### Option 1: Docker Compose (recommended)

```bash
# 1. Copy the environment template
cp .env.example .env

# 2. Generate an admin API key hash
printf '%s' 'my-admin-key' | sha256sum
# Paste the hex output into .env as ADMIN_API_KEY_HASH=<hex>

# 3. Start the stack
docker compose up -d

# 4. Verify readiness
curl http://localhost:8080/health/ready
```

### Option 2: Run the API locally with Docker dependencies

```bash
# Start only the infrastructure services
docker compose up -d sqlserver redis rabbitmq

# Run the API
dotnet run --project src/PaymentGateway.Api
```

The API applies EF migrations automatically on startup. If migrations fail, startup is refused because a payment system must not run against an inconsistent schema.

---

## Database migrations

The application applies migrations automatically. Manual commands are also available:

```bash
# Generate a migration
dotnet ef migrations add YourMigrationName \
  --project src/PaymentGateway.Infrastructure \
  --startup-project src/PaymentGateway.Api

# Apply migrations
dotnet ef database update \
  --project src/PaymentGateway.Infrastructure \
  --startup-project src/PaymentGateway.Api

# Or use the migration helper container
docker compose -f docker-compose.yml run --rm migrations
```

---

## Configuration

Configuration is strongly typed and can be supplied through `appsettings.json` or environment variables.

| Setting | Required | Description |
|---------|----------|-------------|
| `ConnectionStrings:PaymentGateway` | Yes | SQL Server connection string |
| `Redis:ConnectionString` | Yes | Redis connection string |
| `RabbitMq:ConnectionUri` | Yes | RabbitMQ AMQP URI |
| `Security:AdminApiKeyHash` | Production | SHA-256 hash of the admin API key |
| `Security:RequireAdminKey` | Production | Set to `true` in production |
| `Acquirer:FailureMode` | No | `None`, `Decline`, `Transient5xx`, `Timeout`, `Unknown`, `Random` |
| `Fees:CalculateFeesEnabled` | No | Default `false` |
| `Fees:FeePercent` | No | Example: `2.5` for 2.5% |

---

## API examples

### Register a merchant (admin)

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

Example response:

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

Important: The `apiKey` is returned once only. Store it safely; it cannot be retrieved later.

### Create a payment

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

Example response:

```json
{
  "paymentId": "guid-here",
  "status": "Authorized",
  "amount": 100.00,
  "currency": "USD",
  "createdAt": "2026-01-01T00:00:00Z"
}
```

### Settle a payment

```bash
curl -X POST http://localhost:8080/api/v1/payments/{paymentId}/settle \
  -H "Authorization: Bearer pgk_xxxxxxxx..." \
  -H "X-Idempotency-Key: settle-001"
```

### Refund a payment

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

### Get payment details

```bash
curl http://localhost:8080/api/v1/payments/{paymentId} \
  -H "Authorization: Bearer pgk_xxxxxxxx..."
```

### Get account balance

```bash
curl http://localhost:8080/api/v1/accounts \
  -H "Authorization: Bearer pgk_xxxxxxxx..."
```

### Run reconciliation (admin)

```bash
curl -X POST http://localhost:8080/api/v1/admin/reconcile \
  -H "X-Admin-Key: my-admin-key"
```

---

## Idempotency behaviour

Every mutating payment/refund endpoint requires the `X-Idempotency-Key` header.

| Scenario | Response |
|----------|----------|
| First request | Process and return result (201 or 200) |
| Concurrent duplicate while processing | `409 Conflict` with `Retry-After: 2` |
| Completed duplicate with same payload | Replay cached response |
| Same key + different payload | `422` with `idempotency-key-reuse` |
| Missing header | `400` ProblemDetails |

Idempotency keys are scoped by `(MerchantId, OperationType, Key)`, so a payment key cannot collide with a refund key.

---

## Ledger model

The system uses strict double-entry accounting with hard invariants:

- `Σ Debits == Σ Credits` for each ledger transaction
- All entries in a transaction share the same currency
- Ledger entries are append-only; no business-path updates or deletes
- Corrections are created as a new `LedgerTransaction` of type `Correction`

### Posting patterns

#### Payment settlement (no fees)

| Account | Entry | Amount |
|---------|-------|--------|
| `AcquirerReceivable` | Dr | gross |
| `MerchantPayable` | Cr | gross |

#### Payment settlement (with fees)

| Account | Entry | Amount |
|---------|-------|--------|
| `AcquirerReceivable` | Dr | gross |
| `MerchantPayable` | Cr | net (= gross − fee) |
| `FeeRevenue` | Cr | fee |

#### Refund

| Account | Entry | Amount |
|---------|-------|--------|
| `MerchantPayable` | Dr | refund amount |
| `AcquirerReceivable` | Cr | refund amount |

`Account.Balance` is a materialized view; the ledger remains the source of truth. `ReconciliationService` verifies that `Balance == Σ LedgerEntries` and reports discrepancies rather than auto-repairing them.

---

## Outbox architecture

```text
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
  └─ ACK on 2xx / permanent 4xx; NACK + DLQ on retryable cases
        │
        ▼
Merchant
```

This is designed for at-least-once delivery. Consumers must deduplicate. A crash between HTTP delivery and SQL commit can result in duplicate HTTP delivery, so merchants should use `X-Webhook-Id` as their idempotency key.

---

## Webhook architecture

Webhook payloads are signed with HMAC-SHA256:

```text
signature = lowercase_hex(HMAC_SHA256(secret, "{WebhookId}.{UnixTimestamp}.{RawJsonPayload}"))
```

Headers sent with every webhook:

| Header | Description |
|--------|-------------|
| `X-Webhook-Id` | Unique webhook ID; merchants use this as their idempotency key |
| `X-Webhook-Timestamp` | Unix timestamp in seconds |
| `X-Webhook-Signature` | Lowercase hexadecimal HMAC-SHA256 signature |
| `X-Webhook-Event` | Event type, e.g. `payment.settled` |

Replay protection: reject if `|now - timestamp| > Tolerance` (default 300 seconds).

### Retry classification

| HTTP status | Behaviour |
|------------|----------|
| `2xx` | Success, ACK |
| `408`, `429`, `5xx`, network timeout | Retry with exponential backoff |
| `400`, `401`, `403`, `404`, `410`, `422` | Permanent failure → DLQ after `MaxAttempts` |
| Other `4xx` | Retryable, then DLQ |

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

Covers:

- payment state machine
- refund state machine
- ledger balancing
- refund rules
- money arithmetic
- currency validation
- webhook signing
- request fingerprinting
- acquirer simulator
- payment entity behavior

### Integration tests

```bash
dotnet test tests/PaymentGateway.IntegrationTests --configuration Release
```

These spin up real SQL Server, Redis, and RabbitMQ containers with Testcontainers and validate:

- payment creation and persistence
- idempotency replay and key-reuse detection
- ledger balancing and immutability
- reconciliation and corrupted balance detection
- settlement idempotency

### Concurrency tests

```bash
dotnet test tests/PaymentGateway.ConcurrencyTests --configuration Release
```

These validate:

- concurrent duplicate payment requests
- concurrent different-key payment requests
- concurrent refund invariants

---

## Failure and retry semantics

| Scenario | Behaviour | Recovery |
|----------|-----------|----------|
| DB unavailable | Ready checks fail; mutating endpoints return 503 | Reconnect; outbox resumes |
| Redis unavailable | Idempotency falls back to SQL; no corruption | Redis reconnect |
| RabbitMQ unavailable | Outbox accumulates; payments still succeed; webhooks delayed | RabbitMQ reconnect; outbox drains |
| Acquirer timeout | `Processing → Unknown`; recovery queries later | Recovery worker resolves later |
| Acquirer decline | `Processing → Failed` immediately | New payment required |
| Concurrent duplicate | Redis NX + SQL unique → first wins; others 409 | Client retry |
| Same key + different payload | `422 idempotency-key-reuse` | New key required |
| Optimistic concurrency conflict | rowversion triggers retries then 409 | Client retry |
| Webhook timeout | Retry with backoff | Up to `MaxAttempts` |
| Webhook permanent 4xx | DLQ | Manual inspection |
| Consumer crash before ACK | RabbitMQ redelivers; consumer deduplicates by `EventId` | Automatic |
| Stuck `Processing` payment | Recovery worker queries acquirer after heartbeat timeout | No re-authorization |
| Recovery exhausted | Force-fail with reason `RecoveryTimeout` | Manual intervention |

---

## Known limitations

- The acquirer is a simulator. Real integration requires implementing `IAcquirerClient` with a proper HTTP client and secure tokenization flow.
- Card tokens are simulator-only references; the system never stores or logs raw card data.
- Refund processing assumes the same acquirer reference model. Real acquirer APIs may differ.
- No currency conversion is implemented. Each ledger transaction is single-currency.
- No partial settlement. A payment settles in full or fails.
- Health checks validate dependency connectivity, not full end-to-end functionality.

---

## Production hardening recommendations

1. Set `Security:RequireAdminKey=true` and generate a strong admin key hash.
2. Use managed secrets such as Azure Key Vault or AWS Secrets Manager instead of environment variables in production.
3. Add TLS termination in front of the API using nginx, Traefik, or a cloud load balancer.
4. Configure OTLP export to a real collector such as Tempo, Prometheus, or Loki.
5. Configure Seq or Elasticsearch for log aggregation.
6. Alert on `payments_failed_total`, `outbox_pending_count`, `webhook_delivery_failures_total`, and `reconciliation_discrepancies`.
7. Run the reconciliation worker more frequently in production (for example, every 15 minutes).
8. Implement a real acquirer client with proper card tokenization and PCI-DSS-aligned handling.
9. Add per-merchant rate limiting.
10. Add request signing for high-value operations if required by the acquirer.
11. Monitor the `OutboxDeadLetter` table and alert when new entries appear.
12. Run multiple API replicas behind a load balancer; the workers use `IServiceScopeFactory` and atomic claiming so they scale horizontally.

---

## License

Internal. Not for redistribution.
