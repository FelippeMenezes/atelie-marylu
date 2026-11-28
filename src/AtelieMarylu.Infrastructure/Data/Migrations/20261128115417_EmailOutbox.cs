using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtelieMarylu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Orders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutboxEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SkippedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEmails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_OrderId",
                table: "OutboxEmails",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_Pending",
                table: "OutboxEmails",
                column: "NextAttemptAt",
                filter: "\"SentAt\" IS NULL AND \"FailedAt\" IS NULL AND \"SkippedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_Recipient_Kind_CreatedAt",
                table: "OutboxEmails",
                columns: new[] { "Recipient", "Kind", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxEmails");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Orders");
        }
    }
}
