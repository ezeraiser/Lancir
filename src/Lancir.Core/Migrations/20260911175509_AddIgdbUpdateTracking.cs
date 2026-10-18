using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddIgdbUpdateTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IgdbId",
                table: "Games",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IgdbLastUpdatedAtUtc",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdateCheckUtc",
                table: "Games",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IgdbId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "IgdbLastUpdatedAtUtc",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LastUpdateCheckUtc",
                table: "Games");
        }
    }
}
