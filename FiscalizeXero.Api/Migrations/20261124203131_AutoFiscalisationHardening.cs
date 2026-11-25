using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalizeXero.Api.Migrations
{
    /// <inheritdoc />
    public partial class AutoFiscalisationHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAt",
                table: "FiscalFailures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncError",
                table: "Configs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncRunAt",
                table: "Configs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SyncRetryAt",
                table: "Configs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FiscalSuppressions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppUserCompanyId = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalSuppressions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalSuppressions_AppUserCompanies_AppUserCompanyId",
                        column: x => x.AppUserCompanyId,
                        principalTable: "AppUserCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalFailures_NextRetryAt",
                table: "FiscalFailures",
                column: "NextRetryAt");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalSuppressions_AppUserCompanyId_DocumentId",
                table: "FiscalSuppressions",
                columns: new[] { "AppUserCompanyId", "DocumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalSuppressions");

            migrationBuilder.DropIndex(
                name: "IX_FiscalFailures_NextRetryAt",
                table: "FiscalFailures");

            migrationBuilder.DropColumn(
                name: "NextRetryAt",
                table: "FiscalFailures");

            migrationBuilder.DropColumn(
                name: "LastSyncError",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "LastSyncRunAt",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "SyncRetryAt",
                table: "Configs");
        }
    }
}
