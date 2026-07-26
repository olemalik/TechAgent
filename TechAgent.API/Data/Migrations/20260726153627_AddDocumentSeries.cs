using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAgent.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                table: "documents",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "documents");
        }
    }
}
