using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FiscalizeXero.Api.Migrations
{
    /// <summary>
    /// The fiscal PDF choice (Xero stamp, Fiscalize layout, Fiscalize standard invoice) with its paper, the layout packages
    /// the platform served each company, and what each connection reports to the platform's Connected apps page. Existing
    /// connections keep the stamped Xero PDF and each gets its own installation id.
    /// </summary>
    public partial class FiscalLayoutsAndConnectedApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FiscalPdfLayout",
                table: "Configs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "XeroStamp");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPdfFallbackAt",
                table: "Configs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPdfFallbackReason",
                table: "Configs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LayoutPaperSize",
                table: "Configs",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "A4");

            migrationBuilder.AddColumn<string>(
                name: "OrganisationBaseCurrency",
                table: "Configs",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OrganisationCheckedAt",
                table: "Configs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganisationClass",
                table: "Configs",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OrganisationIsDemo",
                table: "Configs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OrganisationShortCode",
                table: "Configs",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PendingPdfFallbacks",
                table: "Configs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PendingPdfsStamped",
                table: "Configs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PendingPdfsWithLayout",
                table: "Configs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PendingPdfsWithStandard",
                table: "Configs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "PlatformInstallationId",
                table: "Configs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<DateTime>(
                name: "PlatformReportedAt",
                table: "Configs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LayoutPackageCaches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppUserCompanyId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaperSize = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PluginLayoutId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Hash = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsStandardDesign = table.Column<bool>(type: "bit", nullable: false),
                    PackageJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LayoutPackageCaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LayoutPackageCaches_AppUserCompanies_AppUserCompanyId",
                        column: x => x.AppUserCompanyId,
                        principalTable: "AppUserCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LayoutPackageCaches_AppUserCompanyId_DocumentType_PaperSize",
                table: "LayoutPackageCaches",
                columns: new[] { "AppUserCompanyId", "DocumentType", "PaperSize" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LayoutPackageCaches");

            migrationBuilder.DropColumn(
                name: "FiscalPdfLayout",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "LastPdfFallbackAt",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "LastPdfFallbackReason",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "LayoutPaperSize",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "OrganisationBaseCurrency",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "OrganisationCheckedAt",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "OrganisationClass",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "OrganisationIsDemo",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "OrganisationShortCode",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "PendingPdfFallbacks",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "PendingPdfsStamped",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "PendingPdfsWithLayout",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "PendingPdfsWithStandard",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "PlatformInstallationId",
                table: "Configs");

            migrationBuilder.DropColumn(
                name: "PlatformReportedAt",
                table: "Configs");
        }
    }
}
