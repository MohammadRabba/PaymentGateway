Fraud Detection Integration

This change adds a "Risk Gate" that integrates an external fraud detection model with the payment flow.

What was added
- `src/PaymentGateway.Domain/Entities/RiskAssessment.cs` — new append-only entity to persist each assessment.
- `src/PaymentGateway.Infrastructure/Persistence/Configurations/RiskAssessmentConfiguration.cs` — EF configuration for the new table.
- `src/PaymentGateway.Domain/Events/PaymentBlockedAsFraudEvent.cs` and `PaymentFlaggedForReviewEvent.cs` — new domain events emitted for blocked/flagged payments.
- `src/PaymentGateway.Application/Common/IFraudDetectionClient.cs` — boundary interface for scoring implementations.
- `src/PaymentGateway.Application/Options/FraudOptions.cs` — configuration options (thresholds, timeouts, service URL, mode).
- `src/PaymentGateway.Application/Risk/RiskGate.cs` — the orchestration service that computes velocity features, calls the fraud client, persists an assessment, and records an audit.
- `src/PaymentGateway.Infrastructure/Fraud/HttpFraudDetectionClient.cs` — simple HTTP client implementation that calls a Python FastAPI scoring service (example).

What you must do next
1. Add the new EF migration to create the `RiskAssessments` table:
   dotnet ef migrations add AddRiskAssessments --project src/PaymentGateway.Infrastructure --startup-project src/PaymentGateway.Api
   dotnet ef database update --project src/PaymentGateway.Infrastructure --startup-project src/PaymentGateway.Api

2. Wire up services in startup:
   - Register `FraudOptions` from configuration: `services.Configure<FraudOptions>(Configuration.GetSection(FraudOptions.SectionName));`
   - Register an implementation of `IFraudDetectionClient`:
     - For HTTP (recommended): `services.AddHttpClient<IFraudDetectionClient, HttpFraudDetectionClient>(...)` and configure `FraudOptions.ServiceUrl`.
     - Or register an ONNX-based implementation if you export a model.
   - Register `RiskGate` as `services.AddScoped<RiskGate>();`

   Add the registrations to `ServiceCollectionExtensions` or `Program.cs`.

3. Update `CreatePaymentHandler` to call `RiskGate.EvaluateAsync(...)` after the initial payment creation and before calling the acquirer. Honor the returned `RiskDecision` (Block/Review/Pass). See code comments in `RiskGate.cs` for example logic.

4. Start the fraud scoring service (if using Python):
   - Implement and start the FastAPI service that exposes `/score` and `/health` endpoints. Place your trained `fraud_model.joblib` next to the service.
   - Set `Fraud:ServiceUrl` in `appsettings.json` to point at the service (e.g. `http://fraud-service:8000`).

5. Run in Shadow mode initially: configure `FraudOptions` to not block (record results only) while validating scores against labelled chargebacks.

Notes and caveats
- The model in `Credit_Card_Fraud_Detection_Final.html` uses features that are not present in this gateway (PCA V1..V10, account balance). Do not deploy the notebook model to production without retraining on real gateway features.
- The Risk Gate is designed to run outside the financial transaction; it does not create ledger entries or call the acquirer when blocking a payment.
- If you need help wiring these registrations into the existing `ServiceCollectionExtensions` or updating `CreatePaymentHandler` I can make those edits for you.
