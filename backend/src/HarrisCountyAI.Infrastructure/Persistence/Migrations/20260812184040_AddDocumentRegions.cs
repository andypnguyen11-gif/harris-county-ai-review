using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarrisCountyAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BoundingBox_Height",
                table: "ValidationReportItems",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BoundingBox_PageNumber",
                table: "ValidationReportItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundingBox_Width",
                table: "ValidationReportItems",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundingBox_X",
                table: "ValidationReportItems",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BoundingBox_Y",
                table: "ValidationReportItems",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "KeyBoundingBox_Height",
                table: "NormalizedDocumentFields",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KeyBoundingBox_PageNumber",
                table: "NormalizedDocumentFields",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "KeyBoundingBox_Width",
                table: "NormalizedDocumentFields",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "KeyBoundingBox_X",
                table: "NormalizedDocumentFields",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "KeyBoundingBox_Y",
                table: "NormalizedDocumentFields",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ValueBoundingBox_Height",
                table: "NormalizedDocumentFields",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValueBoundingBox_PageNumber",
                table: "NormalizedDocumentFields",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ValueBoundingBox_Width",
                table: "NormalizedDocumentFields",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ValueBoundingBox_X",
                table: "NormalizedDocumentFields",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ValueBoundingBox_Y",
                table: "NormalizedDocumentFields",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoundingBox_Height",
                table: "ValidationReportItems");

            migrationBuilder.DropColumn(
                name: "BoundingBox_PageNumber",
                table: "ValidationReportItems");

            migrationBuilder.DropColumn(
                name: "BoundingBox_Width",
                table: "ValidationReportItems");

            migrationBuilder.DropColumn(
                name: "BoundingBox_X",
                table: "ValidationReportItems");

            migrationBuilder.DropColumn(
                name: "BoundingBox_Y",
                table: "ValidationReportItems");

            migrationBuilder.DropColumn(
                name: "KeyBoundingBox_Height",
                table: "NormalizedDocumentFields");

            migrationBuilder.DropColumn(
                name: "KeyBoundingBox_PageNumber",
                table: "NormalizedDocumentFields");

            migrationBuilder.DropColumn(
                name: "KeyBoundingBox_Width",
                table: "NormalizedDocumentFields");

            migrationBuilder.DropColumn(
                name: "KeyBoundingBox_X",
                table: "NormalizedDocumentFields");

            migrationBuilder.DropColumn(
                name: "KeyBoundingBox_Y",
                table: "NormalizedDocumentFields");

            migrationBuilder.DropColumn(
                name: "ValueBoundingBox_Height",
                table: "NormalizedDocumentFields");

            migrationBuilder.DropColumn(
                name: "ValueBoundingBox_PageNumber",
                table: "NormalizedDocumentFields");

            migrationBuilder.DropColumn(
                name: "ValueBoundingBox_Width",
                table: "NormalizedDocumentFields");

            migrationBuilder.DropColumn(
                name: "ValueBoundingBox_X",
                table: "NormalizedDocumentFields");

            migrationBuilder.DropColumn(
                name: "ValueBoundingBox_Y",
                table: "NormalizedDocumentFields");
        }
    }
}
