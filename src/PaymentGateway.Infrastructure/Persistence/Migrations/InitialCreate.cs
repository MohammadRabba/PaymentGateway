using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations;

/// <summary>
/// Initial schema for the payment gateway. All monetary columns use DECIMAL(19, 4).
/// All mutable financial aggregates have a rowversion column for optimistic concurrency.
/// All foreign keys use Restrict delete behavior — financial history is never cascade-deleted.
/// </summary>
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Order matters: referenced tables created first.
        migrationBuilder.CreateTable(
            name: "Merchants",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                ExternalReference = t.Column<string>(maxLength: 100, nullable: false),
                Name = t.Column<string>(maxLength: 200, nullable: false),
                WebhookUrl = t.Column<string>(maxLength: 2048, nullable: false),
                WebhookSecret = t.Column<string>(maxLength: 128, nullable: false),
                ApiKeyHash = t.Column<string>(maxLength: 128, nullable: false),
                Status = t.Column<int>(nullable: false),
                CreatedAt = t.Column<DateTimeOffset>(nullable: false),
                UpdatedAt = t.Column<DateTimeOffset>(nullable: false),
                RowVersion = t.Column<byte[]>(rowVersion: true, nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Merchants", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Accounts",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MerchantId = t.Column<Guid>(nullable: false),
                AccountType = t.Column<int>(nullable: false),
                Currency = t.Column<string>(maxLength: 3, nullable: false),
                Balance = t.Column<decimal>(type: "decimal(19,4)", nullable: false),
                Status = t.Column<int>(nullable: false),
                CreatedAt = t.Column<DateTimeOffset>(nullable: false),
                UpdatedAt = t.Column<DateTimeOffset>(nullable: false),
                RowVersion = t.Column<byte[]>(rowVersion: true, nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Accounts", x => x.Id);
                t.ForeignKey(
                    name: "FK_Accounts_Merchants_MerchantId",
                    column: x => x.MerchantId,
                    principalTable: "Merchants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Payments",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MerchantId = t.Column<Guid>(nullable: false),
                IdempotencyKey = t.Column<string>(maxLength: 128, nullable: false),
                Operation = t.Column<int>(nullable: false),
                Amount = t.Column<decimal>(type: "decimal(19,4)", nullable: false),
                Currency = t.Column<string>(maxLength: 3, nullable: false),
                Status = t.Column<int>(nullable: false),
                AuthCode = t.Column<string>(maxLength: 64, nullable: true),
                AcquirerReference = t.Column<string>(maxLength: 128, nullable: true),
                TotalRefunded = t.Column<decimal>(type: "decimal(19,4)", nullable: false),
                HeartbeatAt = t.Column<DateTimeOffset>(nullable: true),
                CreatedAt = t.Column<DateTimeOffset>(nullable: false),
                AuthorizedAt = t.Column<DateTimeOffset>(nullable: true),
                SettledAt = t.Column<DateTimeOffset>(nullable: true),
                FailedAt = t.Column<DateTimeOffset>(nullable: true),
                FailureReason = t.Column<string>(maxLength: 500, nullable: true),
                RowVersion = t.Column<byte[]>(rowVersion: true, nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Payments", x => x.Id);
                t.ForeignKey(
                    name: "FK_Payments_Merchants_MerchantId",
                    column: x => x.MerchantId,
                    principalTable: "Merchants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Refunds",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                PaymentId = t.Column<Guid>(nullable: false),
                MerchantId = t.Column<Guid>(nullable: false),
                IdempotencyKey = t.Column<string>(maxLength: 128, nullable: false),
                Amount = t.Column<decimal>(type: "decimal(19,4)", nullable: false),
                Currency = t.Column<string>(maxLength: 3, nullable: false),
                Status = t.Column<int>(nullable: false),
                Reason = t.Column<string>(maxLength: 500, nullable: true),
                CreatedAt = t.Column<DateTimeOffset>(nullable: false),
                CompletedAt = t.Column<DateTimeOffset>(nullable: true),
                FailureReason = t.Column<string>(maxLength: 500, nullable: true),
                RowVersion = t.Column<byte[]>(rowVersion: true, nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_Refunds", x => x.Id);
                t.ForeignKey(
                    name: "FK_Refunds_Payments_PaymentId",
                    column: x => x.PaymentId,
                    principalTable: "Payments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                t.ForeignKey(
                    name: "FK_Refunds_Merchants_MerchantId",
                    column: x => x.MerchantId,
                    principalTable: "Merchants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LedgerTransactions",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                PaymentId = t.Column<Guid>(nullable: true),
                RefundId = t.Column<Guid>(nullable: true),
                Type = t.Column<int>(nullable: false),
                Currency = t.Column<string>(maxLength: 3, nullable: false),
                CorrelationId = t.Column<string>(maxLength: 64, nullable: false),
                CreatedAt = t.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_LedgerTransactions", x => x.Id);
                t.ForeignKey(
                    name: "FK_LedgerTransactions_Payments_PaymentId",
                    column: x => x.PaymentId,
                    principalTable: "Payments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                t.ForeignKey(
                    name: "FK_LedgerTransactions_Refunds_RefundId",
                    column: x => x.RefundId,
                    principalTable: "Refunds",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LedgerEntries",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                LedgerTransactionId = t.Column<Guid>(nullable: false),
                AccountId = t.Column<Guid>(nullable: false),
                EntryType = t.Column<int>(nullable: false),
                Amount = t.Column<decimal>(type: "decimal(19,4)", nullable: false),
                Currency = t.Column<string>(maxLength: 3, nullable: false),
                CreatedAt = t.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_LedgerEntries", x => x.Id);
                t.ForeignKey(
                    name: "FK_LedgerEntries_LedgerTransactions_LedgerTransactionId",
                    column: x => x.LedgerTransactionId,
                    principalTable: "LedgerTransactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                t.ForeignKey(
                    name: "FK_LedgerEntries_Accounts_AccountId",
                    column: x => x.AccountId,
                    principalTable: "Accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                EventType = t.Column<string>(maxLength: 64, nullable: false),
                AggregateId = t.Column<Guid>(nullable: false),
                Payload = t.Column<string>(type: "nvarchar(max)", nullable: false),
                OccurredAt = t.Column<DateTimeOffset>(nullable: false),
                Attempts = t.Column<int>(nullable: false),
                NextAttemptAt = t.Column<DateTimeOffset>(nullable: false),
                ProcessedAt = t.Column<DateTimeOffset>(nullable: true),
                ClaimedBy = t.Column<string>(maxLength: 64, nullable: true),
                ClaimedAt = t.Column<DateTimeOffset>(nullable: true),
                LastError = t.Column<string>(maxLength: 2000, nullable: true),
                Status = t.Column<int>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_OutboxMessages", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "OutboxDeadLetters",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                OutboxMessageId = t.Column<Guid>(nullable: false),
                EventType = t.Column<string>(maxLength: 64, nullable: false),
                AggregateId = t.Column<Guid>(nullable: false),
                Payload = t.Column<string>(type: "nvarchar(max)", nullable: false),
                LastError = t.Column<string>(maxLength: 2000, nullable: false),
                Attempts = t.Column<int>(nullable: false),
                OriginalOccurredAt = t.Column<DateTimeOffset>(nullable: false),
                PoisonedAt = t.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_OutboxDeadLetters", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "WebhookDeliveries",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                EventId = t.Column<Guid>(nullable: false),
                WebhookId = t.Column<Guid>(nullable: false),
                MerchantId = t.Column<Guid>(nullable: false),
                EventType = t.Column<string>(maxLength: 64, nullable: false),
                AttemptCount = t.Column<int>(nullable: false),
                Status = t.Column<int>(nullable: false),
                ResponseStatusCode = t.Column<int>(nullable: true),
                DeliveredAt = t.Column<DateTimeOffset>(nullable: true),
                NextAttemptAt = t.Column<DateTimeOffset>(nullable: true),
                LastError = t.Column<string>(maxLength: 2000, nullable: true),
                PayloadHash = t.Column<string>(maxLength: 128, nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AuditRecords",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                Actor = t.Column<string>(maxLength: 128, nullable: false),
                Action = t.Column<string>(maxLength: 64, nullable: false),
                AggregateType = t.Column<string>(maxLength: 64, nullable: false),
                AggregateId = t.Column<Guid>(nullable: false),
                CorrelationId = t.Column<string>(maxLength: 64, nullable: false),
                OccurredAt = t.Column<DateTimeOffset>(nullable: false),
                Metadata = t.Column<string>(type: "nvarchar(max)", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_AuditRecords", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "IdempotencyRecords",
            columns: t => new
            {
                Id = t.Column<Guid>(nullable: false),
                MerchantId = t.Column<Guid>(nullable: false),
                Operation = t.Column<int>(nullable: false),
                Key = t.Column<string>(maxLength: 128, nullable: false),
                RequestHash = t.Column<string>(maxLength: 128, nullable: false),
                State = t.Column<int>(nullable: false),
                StatusCode = t.Column<int>(nullable: true),
                ResponsePayload = t.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = t.Column<DateTimeOffset>(nullable: false),
                CompletedAt = t.Column<DateTimeOffset>(nullable: true),
                ExpiresAt = t.Column<DateTimeOffset>(nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
            });

        // Indexes
        // Merchants
        migrationBuilder.CreateIndex(
            name: "IX_Merchants_ExternalReference",
            table: "Merchants",
            column: "ExternalReference",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Merchants_ApiKeyHash",
            table: "Merchants",
            column: "ApiKeyHash",
            unique: true);

        // Accounts
        migrationBuilder.CreateIndex(
            name: "IX_Accounts_MerchantId_AccountType_Currency",
            table: "Accounts",
            columns: new[] { "MerchantId", "AccountType", "Currency" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_MerchantId",
            table: "Accounts",
            column: "MerchantId");

        // Payments
        migrationBuilder.CreateIndex(
            name: "IX_Payments_MerchantId_IdempotencyKey",
            table: "Payments",
            columns: new[] { "MerchantId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Payments_MerchantId",
            table: "Payments",
            column: "MerchantId");

        migrationBuilder.CreateIndex(
            name: "IX_Payments_Status",
            table: "Payments",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_Payments_AcquirerReference",
            table: "Payments",
            column: "AcquirerReference");

        // Refunds
        migrationBuilder.CreateIndex(
            name: "IX_Refunds_MerchantId_IdempotencyKey",
            table: "Refunds",
            columns: new[] { "MerchantId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Refunds_PaymentId",
            table: "Refunds",
            column: "PaymentId");

        migrationBuilder.CreateIndex(
            name: "IX_Refunds_MerchantId",
            table: "Refunds",
            column: "MerchantId");

        // LedgerTransactions
        migrationBuilder.CreateIndex(
            name: "IX_LedgerTransactions_PaymentId",
            table: "LedgerTransactions",
            column: "PaymentId");

        migrationBuilder.CreateIndex(
            name: "IX_LedgerTransactions_RefundId",
            table: "LedgerTransactions",
            column: "RefundId");

        migrationBuilder.CreateIndex(
            name: "IX_LedgerTransactions_CorrelationId",
            table: "LedgerTransactions",
            column: "CorrelationId");

        // LedgerEntries
        migrationBuilder.CreateIndex(
            name: "IX_LedgerEntries_LedgerTransactionId",
            table: "LedgerEntries",
            column: "LedgerTransactionId");

        migrationBuilder.CreateIndex(
            name: "IX_LedgerEntries_AccountId",
            table: "LedgerEntries",
            column: "AccountId");

        // OutboxMessages — composite index with INCLUDE for the publisher's claim query.
        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Status_NextAttemptAt",
            table: "OutboxMessages",
            columns: new[] { "Status", "NextAttemptAt" })
            .Annotation("SqlServer:Include", new[] { "Id", "EventType", "AggregateId" });

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_AggregateId",
            table: "OutboxMessages",
            column: "AggregateId");

        // OutboxDeadLetters
        migrationBuilder.CreateIndex(
            name: "IX_OutboxDeadLetters_OutboxMessageId",
            table: "OutboxDeadLetters",
            column: "OutboxMessageId");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxDeadLetters_EventType",
            table: "OutboxDeadLetters",
            column: "EventType");

        // WebhookDeliveries
        migrationBuilder.CreateIndex(
            name: "IX_WebhookDeliveries_EventId",
            table: "WebhookDeliveries",
            column: "EventId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WebhookDeliveries_Status_NextAttemptAt",
            table: "WebhookDeliveries",
            columns: new[] { "Status", "NextAttemptAt" });

        migrationBuilder.CreateIndex(
            name: "IX_WebhookDeliveries_MerchantId",
            table: "WebhookDeliveries",
            column: "MerchantId");

        // AuditRecords
        migrationBuilder.CreateIndex(
            name: "IX_AuditRecords_AggregateType_AggregateId",
            table: "AuditRecords",
            columns: new[] { "AggregateType", "AggregateId" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditRecords_CorrelationId",
            table: "AuditRecords",
            column: "CorrelationId");

        migrationBuilder.CreateIndex(
            name: "IX_AuditRecords_OccurredAt",
            table: "AuditRecords",
            column: "OccurredAt");

        // IdempotencyRecords
        migrationBuilder.CreateIndex(
            name: "IX_IdempotencyRecords_MerchantId_Operation_Key",
            table: "IdempotencyRecords",
            columns: new[] { "MerchantId", "Operation", "Key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IdempotencyRecords_State_ExpiresAt",
            table: "IdempotencyRecords",
            columns: new[] { "State", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Drop in reverse dependency order.
        migrationBuilder.DropTable("IdempotencyRecords");
        migrationBuilder.DropTable("AuditRecords");
        migrationBuilder.DropTable("WebhookDeliveries");
        migrationBuilder.DropTable("OutboxDeadLetters");
        migrationBuilder.DropTable("OutboxMessages");
        migrationBuilder.DropTable("LedgerEntries");
        migrationBuilder.DropTable("LedgerTransactions");
        migrationBuilder.DropTable("Refunds");
        migrationBuilder.DropTable("Payments");
        migrationBuilder.DropTable("Accounts");
        migrationBuilder.DropTable("Merchants");
    }
}
