using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamStoreMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgeRating",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Developers",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Languages",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Modes",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Publishers",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScreenshotUrls",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailerThumbnailUrl",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrailerUrl",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Games",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeRating",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Developers",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Languages",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Modes",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Publishers",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ScreenshotUrls",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TrailerThumbnailUrl",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TrailerUrl",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Games");
        }
    }
}
