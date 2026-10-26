using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FinanceAtomicityIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MutationVersion",
                table: "MonthlyRules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ObservedBalance",
                table: "LedgerEntries",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "LedgerEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InitiallyPaid",
                table: "FinanceOperations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "FinanceOperations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidRequestId",
                table: "FinanceOperations",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MutationVersion",
                table: "MonthlyRules");

            migrationBuilder.DropColumn(
                name: "ObservedBalance",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "InitiallyPaid",
                table: "FinanceOperations");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "FinanceOperations");

            migrationBuilder.DropColumn(
                name: "VoidRequestId",
                table: "FinanceOperations");
        }
    }
}
