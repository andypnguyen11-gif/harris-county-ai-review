using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarrisCountyAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddValidationReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ValidationReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValidationReportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Requirement = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ValidationType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtractedValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PageNumber = table.Column<int>(type: "int", nullable: true),
                    ValidationReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationReportItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationReportItems_ValidationReports_ValidationReportId",
                        column: x => x.ValidationReportId,
                        principalTable: "ValidationReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationReportItems_ValidationReportId",
                table: "ValidationReportItems",
                column: "ValidationReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationReports_CaseId",
                table: "ValidationReports",
                column: "CaseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ValidationReportItems");

            migrationBuilder.DropTable(
                name: "ValidationReports");
        }
    }
}
