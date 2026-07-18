using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechAgent.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackToHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FeedbackScore",
                table: "chat_history",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsGolden",
                table: "chat_history",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UserCorrection",
                table: "chat_history",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeedbackScore",
                table: "chat_history");

            migrationBuilder.DropColumn(
                name: "IsGolden",
                table: "chat_history");

            migrationBuilder.DropColumn(
                name: "UserCorrection",
                table: "chat_history");
        }
    }
}
