# Open Payment Gateway & Settlement Engine

A **.NET 9** payment gateway and settlement engine focused on:
* Financial correctness
* Consistency
* Recoverability
* Observability
* Security
* Maintainability

This project demonstrates production-oriented financial systems engineering rather than a simple CRUD application.

It implements:
* Strict double-entry accounting
* Transactional outbox messaging
* Idempotent APIs
* Distributed coordination
* Reliable webhook delivery
* Crash-safe recovery
* Financial reconciliation
* Concurrency-safe state transitions

**Project status:** GitHub / demo project.

> ⚠️ **Note:** The project is designed for correctness and architectural clarity. It is not intended to be deployed directly to production without additional security, compliance, infrastructure, and operational hardening.

---

## 🏛️ Architecture

```text
┌─────────────────────────────────────────────────────────────┐
│                     PaymentGateway.Api                      │
│  Endpoints · Middleware · Authentication · ProblemDetails   │
│  Health Checks · Dependency Injection                       │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                 PaymentGateway.Application                  │
│  Commands · Queries · Handlers · Services · DTOs            │
│  Business Workflows · Persistence Abstractions              │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    PaymentGateway.Domain                    │
│  Entities · Value Objects · State Machines · Rules          │
│  Domain Events · Financial Invariants                       │
└──────────────────────────┴──────────────────────────────────┘
                           ▲
                           │
┌──────────────────────────┴──────────────────────────────────┐
│                PaymentGateway.Infrastructure                │
│  EF Core · SQL Server · Redis · RabbitMQ · Polly            │
│  Workers · Acquirer Simulator · Observability              │
└─────────────────────────────────────────────────────────────┘
