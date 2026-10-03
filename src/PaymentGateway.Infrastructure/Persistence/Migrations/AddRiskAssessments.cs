using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Persistence.Migrations
{
    public partial class AddRiskAssessments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RiskAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MerchantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<decimal>(type: "decimal(8,6)", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ModelVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    AssessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskAssessments_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RiskAssessments_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_PaymentId",
                table: "RiskAssessments",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_MerchantId",
                table: "RiskAssessments",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_Decision",
                table: "RiskAssessments",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_RiskAssessments_AssessedAt",
                table: "RiskAssessments",
                column: "AssessedAt");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RiskAssessments");
        }
    }
}
