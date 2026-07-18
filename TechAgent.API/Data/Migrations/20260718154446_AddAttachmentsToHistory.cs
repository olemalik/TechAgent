using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAgent.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentsToHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentContentType",
                table: "chat_history",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentName",
                table: "chat_history",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                table: "chat_history",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentContentType",
                table: "chat_history");

            migrationBuilder.DropColumn(
                name: "AttachmentName",
                table: "chat_history");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                table: "chat_history");
        }
    }
}
