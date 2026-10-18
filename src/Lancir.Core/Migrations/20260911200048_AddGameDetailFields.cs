using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGameDetailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundImagePath",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CommunityRating",
                table: "Games",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Genres",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseDateUtc",
                table: "Games",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundImagePath",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "CommunityRating",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Genres",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "ReleaseDateUtc",
                table: "Games");
        }
    }
}
