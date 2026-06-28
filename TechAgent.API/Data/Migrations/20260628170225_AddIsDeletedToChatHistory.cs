using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAgent.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDeletedToChatHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "document_chunks");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "chat_history",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "chat_history");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "document_chunks",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }
    }
}
