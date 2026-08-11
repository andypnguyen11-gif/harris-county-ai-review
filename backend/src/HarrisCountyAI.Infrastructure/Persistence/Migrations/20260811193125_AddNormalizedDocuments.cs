using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarrisCountyAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NormalizedDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RawText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedDocumentFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsChecked = table.Column<bool>(type: "bit", nullable: true),
                    IsSigned = table.Column<bool>(type: "bit", nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    PageNumber = table.Column<int>(type: "int", nullable: true),
                    NormalizedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedDocumentFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedDocumentFields_NormalizedDocuments_NormalizedDocumentId",
                        column: x => x.NormalizedDocumentId,
                        principalTable: "NormalizedDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NormalizedDocumentPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalizedDocumentPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NormalizedDocumentPages_NormalizedDocuments_NormalizedDocumentId",
                        column: x => x.NormalizedDocumentId,
                        principalTable: "NormalizedDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedDocumentFields_NormalizedDocumentId",
                table: "NormalizedDocumentFields",
                column: "NormalizedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedDocumentPages_NormalizedDocumentId",
                table: "NormalizedDocumentPages",
                column: "NormalizedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedDocuments_CaseId",
                table: "NormalizedDocuments",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_NormalizedDocuments_DocumentId",
                table: "NormalizedDocuments",
                column: "DocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NormalizedDocumentFields");

            migrationBuilder.DropTable(
                name: "NormalizedDocumentPages");

            migrationBuilder.DropTable(
                name: "NormalizedDocuments");
        }
    }
}
