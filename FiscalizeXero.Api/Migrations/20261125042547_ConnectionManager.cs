using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalizeXero.Api.Migrations
{
    /// <inheritdoc />
    public partial class ConnectionManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectedByName",
                table: "Configs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConnectedByUserId",
                table: "Configs",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectedByName",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "ConnectedByUserId",
                table: "Configs");
        }
    }
}
