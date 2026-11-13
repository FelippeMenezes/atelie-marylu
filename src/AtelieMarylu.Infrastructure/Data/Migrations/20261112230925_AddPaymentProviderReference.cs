using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtelieMarylu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentProviderReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderReference",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderReference",
                table: "Payments");
        }
    }
}
