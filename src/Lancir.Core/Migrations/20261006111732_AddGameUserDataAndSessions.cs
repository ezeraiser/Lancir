using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGameUserDataAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFavorite",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPlayedUtc",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LaunchArguments",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlayCount",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UserRating",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UserTags",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkingDirectory",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlaySessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GameId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationSeconds = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaySessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaySessions_GameId_StartedAtUtc",
                table: "PlaySessions",
                columns: new[] { "GameId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaySessions");

            migrationBuilder.DropColumn(
                name: "IsFavorite",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LastPlayedUtc",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "LaunchArguments",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "PlayCount",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "UserRating",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "UserTags",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "WorkingDirectory",
                table: "Games");
        }
    }
}
