# Open Payment Gateway & Settlement Engine

A production-oriented **.**NET** 9 payment gateway and settlement engine** designed around correctness, consistency, recoverability, observability, security, and maintainability.

This project is not a **CRUD** demo. It models the realities of financial systems: strict double-entry accounting, transactional outbox publishing, idempotent APIs, distributed coordination, reliable webhook delivery, crash-safe recovery, fraud-risk assessment, and financial reconciliation.

---

## Why this project exists

The goal is to build a payment platform that behaves like a real financial system rather than a toy **API**.

The implementation emphasizes:

- Financial correctness
- Consistency under concurrency
- Recoverability and safe retries
- Fraud-risk assessment before authorization
- Observability into runtime health and failures
- Security-minded configuration and **API** protection
- Maintainable architecture with explicit boundaries
- Transactional event publishing
- Idempotent payment processing
- Reliable webhook delivery
- Financial reconciliation

---

## Architecture overview

```text
┌──────────────────────────────────────────────────────────────────────┐
│                         PaymentGateway.Api                            │
│ **HTTP** endpoints, middleware, auth, ProblemDetails, health, DI         │
└──────────────────────────────┬───────────────────────────────────────┘
    │
    ▼
┌──────────────────────────────────────────────────────────────────────┐
│                       PaymentGateway.Application                      │
│                                                                      │
│ Handlers, DTOs, use cases, idempotency, risk orchestration,         │
│ persistence abstractions, auditing, payment/settlement workflows    │
└──────────────┬───────────────────────────────┬───────────────────────┘
    │                               │
    ▼                               ▼
┌──────────────────────────┐       ┌───────────────────────────────────┐
│   PaymentGateway.Domain  │       │   PaymentGateway.Infrastructure  │
│                          │       │                                   │
│ Entities, value objects, │       │ EF Core, **SQL** Server, Redis,      │
│ state machines, ledger   │       │ RabbitMQ, Polly, **HTTP** clients,   │
│ rules, domain events     │       │ workers, outbox, fraud client    │
└──────────────────────────┘       └───────────────────────────────────┘
    │
    ┌─────────────────────────┼─────────────────────────┐
    │                         │                         │
    ▼                         ▼                         ▼
    **SQL** Server                  Redis                   RabbitMQ
    System of record         Coordination/cache          Event delivery
```

### Payment risk flow

Fraud assessment occurs after the payment has been persisted in `Processing` state and before the acquirer is called.

```text
CreatePaymentHandler
    │
    ▼
Persist Payment = Processing
    │
    ▼
    RiskGate
    │
    ├───────────────┐
    │               │
    ▼               ▼
    Fraud Service    Fraud disabled
    │               │
    ▼               │
 RiskAssessment         │
    │               │
    ┌────┼────┐          │
    │    │    │          │
    ▼    ▼    ▼          ▼
 Pass Review Block     Pass
    │     │     │         │
    │     │     └──────► Stop
    │     │
    │     └────────────► Flag + continue
    │
    └──────────────────► Acquirer
```

## Project structure

```text payment-gateway/ ├── src/ │   ├── PaymentGateway.Domain/ │   │   └── # Pure C# domain model, entities, value objects, events │   │ │   ├── PaymentGateway.Application/ │   │   └── # Use cases, handlers, abstractions, risk orchestration │   │ │   ├── PaymentGateway.Infrastructure/ │   │   └── # EF Core, **SQL** Server, Redis, RabbitMQ, **HTTP** clients, workers │   │ │   └── PaymentGateway.Api/ │       └── # **ASP**.**NET** Core app, middleware, endpoints, configuration │ ├── tests/ │   ├── PaymentGateway.UnitTests/ │   ├── PaymentGateway.IntegrationTests/ │   └── PaymentGateway.ConcurrencyTests/ │ ├── deploy/docker/ ├── docker-compose.yml ├── .env.example ├── **README**.md └── **LICENSE** ```

## Key design decisions

### SQL Server is the authoritative ledger

All financial mutations run inside serializable transactions using `**UPDLOCK**`, `**HOLDLOCK**`, and database-level constraints where appropriate.

Redis is used for coordination and fast-path idempotency, not as the financial system of record.

This means:

- Financial correctness is protected by database transactions.
- Redis provides idempotency and lock coordination.
- Ledger truth remains in **SQL** Server.
- Database constraints provide a final correctness boundary.

### Authorization is not the same as settlement

The payment lifecycle is intentionally split.

`CreatePaymentHandler` moves a payment to `Authorized` once the acquirer approves it.

`SettlePaymentHandler` is a separate operation that posts the double-entry ledger atomically.

This keeps payment authorization separate from final accounting.

```text
Payment creation
    │
    ▼
    Processing
    │
    ├── Fraud Block ──► Failed
    │
    ├── Fraud Review ─► Flagged + continue
    │
    ▼
    Acquirer
    │
    ├── Approved ────► Authorized
    ├── Declined ────► Failed
    └── Unknown ─────► Unknown
    │
    ▼
    Recovery Worker
    │
    ▼
    Resolve Acquirer State
    │
    ▼
    Settlement
    │
    ▼
    Settled
```

## Fraud-risk integration

The payment gateway includes a fraud-risk assessment layer designed to evaluate a payment before it is sent to the acquirer.

The integration is deliberately separated from the core payment domain through an application-level abstraction:

```text
CreatePaymentHandler
    │
    ▼
    RiskGate
    │
    ▼
IFraudDetectionClient
    │
    ▼
HttpFraudDetectionClient
    │
    ▼
### External Fraud Detection Service
```

This keeps the payment application independent from the underlying fraud-model implementation.

### Risk assessment

Each payment can produce a `RiskAssessment` containing the fraud score, decision, model information, and assessment metadata.

The main decisions are:

| Decision | Behaviour |
|----------|-----------|
| Pass | Continue to the acquirer |
| Review | Persist the risk assessment, publish a review event, and continue to the acquirer |
| Block | Mark the payment as failed, publish a fraud-block event, complete idempotency, and do not call the acquirer |
| Unavailable | Fraud service could not provide a decision; behaviour follows `FailClosedMode` |

The `Review` path currently uses flag-and-proceed semantics. This allows the transaction to continue while recording the risk signal for downstream review and future model improvement.

### Fraud decision flow

```text
Payment = Processing
    │
    ▼
    RiskGate
    │
    ▼
FraudDetectionClient
    │
    ▼
    Fraud Score
    │
    ├───────────────┐
    │               │
    ▼               ▼
    Thresholds      Service failure
    │               │
    ┌───┼───┐           ▼
    │   │   │      FailClosedMode
    │   │   │        ┌────┴────┐
    │   │   │        │         │
    ▼   ▼   ▼        ▼         ▼
 Pass Review Block  Block     Pass
    │    │    │       │         │
    │    │    └───────┘         │
    │    │                      │
    │    └─ Publish review      │
    │       event + continue    │
    │                           │
    └───────────────────────────┘
    │
    ▼
    Acquirer
```

### Fraud configuration

Fraud detection is configured through strongly typed `FraudOptions`.

Typical configuration includes:

| Setting | Description |
|---------|-------------|
| `Fraud:Enabled` | Enables or disables fraud assessment |
| `Fraud:ServiceUrl` | Base URL of the fraud detection service |
| `Fraud:Timeout` | HTTP request timeout |
| `Fraud:BlockThreshold` | Score at or above which the payment is blocked |
| `Fraud:ReviewThreshold` | Score at or above which the payment is flagged for review |
| `Fraud:FailClosedMode` | Determines behaviour when the fraud service is unavailable |

Example:

```json
{
    *Fraud*: {
    *Enabled*: true,
    *ServiceUrl*: *[http://fraud-service:**8000***,](http://fraud-service:**8000***,)
    *Timeout*: *00:00:02*,
    *BlockThreshold*: 0.96,
    *ReviewThreshold*: 0.50,
    *FailClosedMode*: *Pass"
    }
}
```

Environment variables can also be used:

```bash Fraud__Enabled=true Fraud__ServiceUrl=[http://fraud-service:**8000**](http://fraud-service:**8000**) Fraud__Timeout=00:00:02 Fraud__BlockThreshold=0.96 Fraud__ReviewThreshold=0.50 Fraud__FailClosedMode=Pass ```

### Fail-open vs fail-closed

The default configuration is fail-open:

```text
Fraud service unavailable
    │
    ▼
RiskDecision.Unavailable
    │
    ▼
Payment continues to acquirer
```

When configured as `Block`:

```text
Fraud service unavailable
    │
    ▼
RiskDecision.Block
    │
    ▼
Payment failed
    │
    ▼
Acquirer **NOT** called
```

Fail-closed behaviour should be selected according to the deployment's risk and availability requirements.

### Fraud events

Fraud-related decisions use the existing transactional outbox architecture.

The integration introduces:

- `PaymentBlockedAsFraudEvent`
- `PaymentFlaggedForReviewEvent`

A block operation persists the payment state, audit record, outbox event, and idempotency completion atomically.

```text **SQL** Transaction ├── Payment.Status = Failed ├── RiskAssessment ├── AuditRecord ├── PaymentBlockedAsFraudEvent └── Idempotency completion ```

This prevents a situation where the payment is blocked but the corresponding event or idempotency state is lost.

### Fraud model deployment

The current application uses an **HTTP**-based fraud client:

```text
PaymentGateway
    │
    │ **HTTP**
    ▼
### Fraud Detection Service
    │
    ▼
Python / FastAPI
    │
    ▼
Exported ML model
```

This keeps model serving independent from the payment gateway process.

A typical fraud service exposes:

```text **GET**  /health **POST** /score ```

The gateway does not directly depend on Python or a specific machine-learning framework.

An alternative future implementation could expose an `IFraudDetectionClient` backed by an in-process **ONNX** model.

### Shadow and review operation

Fraud detection can be introduced progressively.

A recommended rollout sequence is:

## Enable scoring without blocking.

## Persist `RiskAssessment` records. ## Monitor score distributions and service reliability. ## Use `Review` for suspicious transactions. ## Collect confirmed fraud/legitimate labels. ## Retrain and validate the model. ## Introduce a controlled `Block` threshold. ## Monitor false positives, false negatives, service availability, and blocked-payment volume.

The payment gateway should remain the authoritative source for payment state; the fraud model is a decision-support component.

See `docs/FRAUD_INTEGRATION.md` for implementation and deployment details.

## Unknown acquirer outcomes are first-class

If the acquirer times out or returns an unknown outcome, the payment transitions to `Unknown`.

A recovery worker later queries the acquirer using a deterministic idempotency key to resolve the state.

This prevents accidental re-authorization of a transaction whose original outcome is unknown.

## Idempotency is layered

Each mutating operation includes multiple levels of protection:

- Redis fast path using `**SET** NX PX`.
- **SQL** unique constraint on `(MerchantId, Operation, IdempotencyKey)`.
- Payment state checks to prevent reprocessing already-settled state.

This helps guard against duplicate processing and operation collisions.

## Transactional outbox

Every financial mutation writes an `OutboxMessage` in the same EF transaction as the financial state changes.

```text
Payment mutation
    │
    ▼
**SQL** Transaction
├── Payment state
├── Ledger entries
├── Account balance
├── Audit record
└── OutboxMessage
    │
    ▼
    **COMMIT**
    │
    ▼
OutboxPublisherWorker
    │
    ▼
    RabbitMQ
```

The outbox publisher provides at-least-once delivery.

This prevents events from being lost between a database commit and downstream event publication.

## Outbox architecture

```text
### Payment Mutation
    │
    ├─ LedgerEntries
    ├─ Account.Balance update
    ├─ Payment.Status update
    ├─ OutboxMessage insert
    └─ AuditRecord insert
    │
    ▼
**COMMIT**
    │
    ▼
OutboxPublisherWorker
    ├─ Claim messages atomically
    ├─ Publish to RabbitMQ
    ├─ Mark Published on success
    ├─ Retry with exponential backoff
    └─ Poison after MaxAttempts
    │
    ▼
    OutboxDeadLetter
    │
    ▼
RabbitMQ
    │
    ▼
WebhookEventConsumer
    ├─ Consumer-side idempotency by EventId
    ├─ Look up merchant webhook **URL** + secret
    ├─ **HMAC**-**SHA256** sign + **POST**
    └─ **ACK** / **NACK** according to delivery result
```

This architecture is designed for at-least-once delivery.

Consumers must therefore deduplicate events.

## Webhook architecture

Webhook payloads are signed using **HMAC**-**SHA256**.

```text
signature =
HMAC_SHA256(
    secret,
    *{WebhookId}.{UnixTimestamp}.{RawJsonPayload}*
)
```

The resulting signature is encoded as lowercase hexadecimal.

### Webhook headers

| Header | Description |
|--------|-------------|
| `X-Webhook-Id` | Unique webhook ID; merchants should use this as their idempotency key |
| `X-Webhook-Timestamp` | Unix timestamp in seconds |
| `X-Webhook-Signature` | Lowercase hexadecimal HMAC-SHA256 signature |
| `X-Webhook-Event` | Event type, e.g. `payment.settled` |

### Replay protection

Consumers should reject requests when:

`|current_time - X-Webhook-Timestamp| > tolerance`

The default tolerance is **300** seconds.

### Retry classification

| HTTP status | Behaviour |
|-------------|-----------|
| 2xx | Success, ACK |
| 408, 429, 5xx | Retry |
| Network timeout | Retry |
| 400, 401, 403, 404, 410, 422 | Permanent failure → DLQ |
| Other 4xx | Retry, then DLQ |

A crash between **HTTP** delivery and the corresponding **SQL** state update can result in duplicate delivery. Merchants should therefore treat `X-Webhook-Id` as an idempotency key.

## Ledger model

The system uses strict double-entry accounting with hard invariants.

For every ledger transaction:

```text Σ Debits == Σ Credits ```

Additional invariants:

- All entries in a transaction share the same currency.
- Ledger entries are append-only.
- Business paths cannot update or delete existing ledger entries.
- Corrections are represented as new `LedgerTransaction` records.
- `Account.Balance` is a materialized view of ledger state.

The ledger remains the source of truth.

`ReconciliationService` verifies that:

```text Account.Balance == Σ LedgerEntries ```

Discrepancies are reported rather than silently repaired.

### Posting patterns

#### Payment settlement — no fees

| Account | Entry | Amount |
|---------|-------|--------|
| `AcquirerReceivable` | Dr | gross |
| `MerchantPayable` | Cr | gross |

#### Payment settlement — with fees

| Account | Entry | Amount |
|---------|-------|--------|
| `AcquirerReceivable` | Dr | gross |
| `MerchantPayable` | Cr | net |
| `FeeRevenue` | Cr | fee |

Where:

```text net = gross - fee ```

#### Refund

| Account | Entry | Amount |
|---------|-------|--------|
| `MerchantPayable` | Dr | refund amount |
| `AcquirerReceivable` | Cr | refund amount |

## Prerequisites

- .**NET** 9 **SDK**
- Docker
- Docker Compose
- Optional: `dotnet-ef` for manual migration work

Install the EF **CLI** if required:

```bash dotnet tool install --global dotnet-ef ```

## Local setup

### Option 1: Docker Compose

Recommended for local development.

#### 1. Copy the environment template

```bash cp .env.example .env ```

#### 2. Generate an admin API key hash

```bash printf '%s' 'my-admin-key' | sha256sum ```

Paste the resulting hexadecimal value into:

```text ADMIN_API_KEY_HASH=<hex> ```

#### 3. Start the stack

```bash docker compose up -d ```

#### 4. Verify readiness

```bash curl [http://localhost:**8080**/health/ready](http://localhost:**8080**/health/ready) ```

### Option 2: Run the API locally

Start only the infrastructure dependencies:

```bash docker compose up -d sqlserver redis rabbitmq ```

Then run the **API**:

```bash dotnet run --project src/PaymentGateway.Api ```

The **API** applies EF migrations automatically on startup.

If migrations fail, startup is refused because a payment system must not run against an inconsistent schema.

## Database migrations

The application applies migrations automatically on startup.

Manual commands are also available.

### Generate a migration

```bash
dotnet ef migrations add YourMigrationName \
    --project src/PaymentGateway.Infrastructure \
    --startup-project src/PaymentGateway.Api
```

### Apply migrations

```bash
dotnet ef database update \
    --project src/PaymentGateway.Infrastructure \
    --startup-project src/PaymentGateway.Api
```

### Migration helper container

```bash docker compose -f docker-compose.yml run --rm migrations ```

The fraud-risk integration includes the `AddRiskAssessments` migration, which creates the database structures required for risk assessments.

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
| `Fraud:Enabled` | No | Enables fraud-risk assessment |
| `Fraud:ServiceUrl` | When enabled | Fraud detection service base URL |
| `Fraud:Timeout` | No | Fraud HTTP request timeout |
| `Fraud:BlockThreshold` | No | Score threshold for blocking |
| `Fraud:ReviewThreshold` | No | Score threshold for review |
| `Fraud:FailClosedMode` | No | `Pass` or `Block` when fraud service is unavailable |

## API examples

### Register a merchant

Admin operation:

```bash
curl -X **POST** [http://localhost:**8080**/api/v1/merchants](http://localhost:**8080**/api/v1/merchants) \
    -H *X-Admin-Key: my-admin-key* \
    -H *X-Idempotency-Key: merchant-**001*** \
    -H *Content-Type: application/json* \
    -d '{
    *externalReference*: *merchant-**001***,
    *name*: *Acme Corp*,
    *webhookUrl*: *[https://example.com/webhook*](https://example.com/webhook*)
    }'
```

Example response:

```json
{
    *id*: *guid-here*,
    *externalReference*: *merchant-**001***,
    *name*: *Acme Corp*,
    *webhookUrl*: *[https://example.com/webhook*,](https://example.com/webhook*,)
    *status*: *Active*,
    *createdAt*: ***2026**-01-**01T00**:00:**00Z***,
    *apiKey*: *pgk_xxxxxxxx...*
}
```

**Important:** The `apiKey` is returned once only. Store it safely; it cannot be retrieved later.

### Create a payment

```bash
curl -X **POST** [http://localhost:**8080**/api/v1/payments](http://localhost:**8080**/api/v1/payments) \
    -H *Authorization: Bearer pgk_xxxxxxxx...* \
    -H *X-Idempotency-Key: payment-**001*** \
    -H *Content-Type: application/json* \
    -d '{
    *amount*: **100**.00,
    *currency*: *USD*,
    *cardToken*: *tok_test_card*
    }'
```

Example response:

```json
{
    *paymentId*: *guid-here*,
    *status*: *Authorized*,
    *amount*: **100**.00,
    *currency*: *USD*,
    *createdAt*: ***2026**-01-**01T00**:00:**00Z***
}
```

Depending on fraud configuration and the returned risk decision, the payment may instead be blocked before the acquirer is called.

### Settle a payment

```bash
curl -X **POST** [http://localhost:**8080**/api/v1/payments/{paymentId}/settle](http://localhost:**8080**/api/v1/payments/{paymentId}/settle) \
    -H *Authorization: Bearer pgk_xxxxxxxx...* \
    -H *X-Idempotency-Key: settle-**001***
```

### Refund a payment

```bash
curl -X **POST** [http://localhost:**8080**/api/v1/payments/{paymentId}/refunds](http://localhost:**8080**/api/v1/payments/{paymentId}/refunds) \
    -H *Authorization: Bearer pgk_xxxxxxxx...* \
    -H *X-Idempotency-Key: refund-**001*** \
    -H *Content-Type: application/json* \
    -d '{
    *amount*: 50.00,
    *currency*: *USD*,
    *reason*: *Customer request*
    }'
```

### Get payment details

```bash curl [http://localhost:**8080**/api/v1/payments/{paymentId}](http://localhost:**8080**/api/v1/payments/{paymentId}) \ -H *Authorization: Bearer pgk_xxxxxxxx...* ```

### Get account balance

```bash curl [http://localhost:**8080**/api/v1/accounts](http://localhost:**8080**/api/v1/accounts) \ -H *Authorization: Bearer pgk_xxxxxxxx...* ```

### Run reconciliation

Admin operation:

```bash curl -X **POST** [http://localhost:**8080**/api/v1/admin/reconcile](http://localhost:**8080**/api/v1/admin/reconcile) \ -H *X-Admin-Key: my-admin-key* ```

## Idempotency behaviour

Every mutating payment/refund endpoint requires the `X-Idempotency-Key` header.

| Scenario | Response |
|----------|----------|
| First request | Process and return result (201 or 200) |
| Concurrent duplicate while processing | `409 Conflict` with `Retry-After: 2` |
| Completed duplicate with same payload | Replay cached response |
| Same key + different payload | `422` with `idempotency-key-reuse` |
| Missing header | `400` ProblemDetails |

Idempotency keys are scoped by:

```text (MerchantId, OperationType, IdempotencyKey) ```

Therefore a payment key cannot collide with a refund key.

Fraud-blocked payments also complete their idempotency record as part of the same transactional block operation, preventing the request from being accidentally reprocessed.

## Testing

### Run all tests

```bash dotnet test --configuration Release ```

### Unit tests

```bash dotnet test tests/PaymentGateway.UnitTests --configuration Release ```

The unit suite covers:

- Payment state machine
- Refund state machine
- Ledger balancing
- Refund rules
- Money arithmetic
- Currency validation
- Webhook signing
- Request fingerprinting
- Acquirer simulator
- Payment entity behavior
- Fraud-risk orchestration
- Fraud allow/block/review behaviour
- Fraud service failure behaviour
- Disabled fraud detection behaviour
- **HTTP** fraud-client behaviour

The fraud-risk integration uses an EF Core InMemory test database and a test `IPaymentGatewayDbContext` adapter so that transaction-sensitive handler logic can be exercised without depending on SQLite **SQL** dialect behaviour.

### Integration tests

```bash dotnet test tests/PaymentGateway.IntegrationTests --configuration Release ```

These use real **SQL** Server, Redis, and RabbitMQ containers with Testcontainers and validate:

- Payment creation and persistence
- Idempotency replay and key-reuse detection
- Ledger balancing and immutability
- Reconciliation
- Corrupted balance detection
- Settlement idempotency
- Infrastructure failure behaviour

### Concurrency tests

```bash dotnet test tests/PaymentGateway.ConcurrencyTests --configuration Release ```

These validate:

- Concurrent duplicate payment requests
- Concurrent different-key payment requests
- Concurrent refund invariants
- Database concurrency handling

## Failure and retry semantics

| Scenario | Behaviour | Recovery |
|----------|-----------|----------|
| DB unavailable | Readiness checks fail; mutating endpoints return 503 | Reconnect |
| Redis unavailable | Idempotency falls back to SQL | Redis reconnect |
| RabbitMQ unavailable | Outbox accumulates; payments continue | RabbitMQ reconnect; outbox drains |
| Fraud service unavailable, fail-open | Risk decision becomes unavailable/pass-through | Monitor and restore fraud service |
| Fraud service unavailable, fail-closed | Payment is blocked | Restore fraud service |
| Acquirer timeout | `Processing` → `Unknown` | Recovery worker resolves later |
| Acquirer decline | `Processing` → `Failed` | New payment required |
| Concurrent duplicate | Redis NX + SQL unique constraint | Client retries |
| Same key + different payload | `422` `idempotency-key-reuse` | New key required |
| Optimistic concurrency conflict | `rowversion` triggers retries then 409 | Client retry |
| Webhook timeout | Retry with backoff | Up to `MaxAttempts` |
| Webhook permanent 4xx | DLQ | Manual inspection |
| Consumer crash before ACK | RabbitMQ redelivers | Consumer deduplicates by `EventId` |
| Stuck `Processing` payment | Recovery worker queries acquirer | No re-authorization |
| Recovery exhausted | Force-fail with `RecoveryTimeout` | Manual intervention |

## Observability and operational signals

Important operational signals include:

- `payments_failed_total`
- `outbox_pending_count`
- `webhook_delivery_failures_total`
- `reconciliation_discrepancies`
- `fraud_assessments_total`
- `fraud_blocks_total`
- `fraud_reviews_total`
- `fraud_service_failures_total`
- `fraud_service_latency`

Fraud-related monitoring should distinguish between:

- Fraud decisions
- Fraud-service availability
- Blocked transactions
- Review decisions
- Unavailable decisions
- Payment outcomes

A high fraud-service failure rate should not be confused with a high fraud rate.

## Known limitations

- The acquirer is a simulator.
    - Real acquirer integration requires implementing `IAcquirerClient` with a proper **HTTP** client and secure tokenization flow.
    - Card tokens are simulator-only references; the system never stores or logs raw card data.
- Refund processing assumes the same acquirer reference model. Real acquirer APIs may differ.
- No currency conversion is implemented.
  - Each ledger transaction is single-currency.
- No partial settlement is implemented.
  - A payment settles in full or fails.
- Health checks validate dependency connectivity, not full end-to-end business functionality.
- The fraud service is currently an external **HTTP** dependency.
    - The fraud model itself is not embedded in the payment gateway process.
    - Fraud decisions depend on the quality, availability, and calibration of the deployed model.
    - Fraud Review currently uses flag-and-proceed semantics.
    - Model retraining and confirmed fraud-label collection are outside the gateway itself.

## Production hardening recommendations

### Security

- Set `Security:RequireAdminKey=true`.
- Generate a strong admin key.
- Use managed secrets such as Azure Key Vault or **AWS** Secrets Manager rather than plain environment variables.
- Add **TLS** termination in front of the **API** using nginx, Traefik, or a cloud load balancer.
- Never log raw card data.
- Use secure tokenization for real payment-card integrations.
- Review fraud-service authentication and network isolation before production deployment.

### Infrastructure

- Run multiple **API** replicas behind a load balancer.
- Use managed **SQL** Server, Redis, and RabbitMQ where appropriate.
- Configure appropriate database backups and point-in-time recovery.
- Monitor the `OutboxDeadLetter` table.
- Alert when new dead-letter entries appear.
- Ensure fraud-service availability is monitored independently from the payment gateway.

### Observability

Configure **OTLP** export to a real collector such as:

- Tempo
- Prometheus
- Loki

Configure centralized log aggregation using systems such as:

- Seq
- Elasticsearch

Alert on:

- `payments_failed_total`
- `outbox_pending_count`
- `webhook_delivery_failures_total`
- `reconciliation_discrepancies`
- `fraud_service_failures_total`
- `fraud_blocks_total`
- `fraud_review_rate`

### Reconciliation

Run the reconciliation worker more frequently in production, for example every 15 minutes, depending on operational requirements.

### Fraud system

Before enabling production blocking:

- Validate model performance on representative gateway data.
- Monitor false-positive and false-negative rates.
- Establish a process for confirmed fraud labels.
- Monitor score distribution drift.
- Version deployed models.
- Record the model version with each risk assessment.
- Establish a rollback strategy for model releases.
- Monitor fraud-service latency and availability.
- Start with shadow/review operation before introducing blocking thresholds.
- Review `FailClosedMode` against the business's availability and fraud-loss requirements.

### Scaling

Workers use `IServiceScopeFactory` and atomic claiming where appropriate so that multiple application replicas can operate concurrently.

Ensure that:

- Database constraints remain authoritative.
- Outbox claiming remains atomic.
- Idempotency remains enforced at the **SQL** layer.
- Consumers deduplicate events.
- Fraud requests have appropriate timeouts and circuit-breaking behaviour.

## Design principles

The project is built around several core principles.

**Database correctness over cache correctness**

Redis can accelerate coordination, but **SQL** Server remains authoritative.

**Idempotency everywhere**

Retries are expected rather than treated as exceptional.

**Unknown states are explicit**

The system does not convert uncertainty into success or failure without a recovery mechanism.

**Events are transactional**

Business state and the corresponding outbox event are committed together.

**Financial records are append-only**

Corrections create new accounting records rather than rewriting history.

**External dependencies are isolated**

Acquirer and fraud-service integrations are represented by application-level abstractions.

**Fraud does not own payment state**

The fraud system produces a risk assessment; the payment gateway remains responsible for the authoritative payment lifecycle.

**Recovery is part of the design**

Workers resolve unknown payments, drain outboxes, retry webhooks, and support reconciliation.

## License

Internal. Not for redistribution. ````
##👤 Author
Mohammad Rabba.
