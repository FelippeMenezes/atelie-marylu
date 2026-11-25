using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtelieMarylu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueOpenCancellationRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CancellationRequests_OrderId_Open",
                table: "CancellationRequests",
                column: "OrderId",
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CancellationRequests_OrderId_Open",
                table: "CancellationRequests");
        }
    }
}
