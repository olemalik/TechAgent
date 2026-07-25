using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAgent.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConflictsJson",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FingerprintJson",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SupersededAt",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConflictsJson",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "FingerprintJson",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "documents");
        }
    }
}
