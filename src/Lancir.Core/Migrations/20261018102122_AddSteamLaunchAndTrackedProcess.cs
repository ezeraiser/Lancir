using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamLaunchAndTrackedProcess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LaunchViaSteam",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TrackedProcessName",
                table: "Games",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaunchViaSteam",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "TrackedProcessName",
                table: "Games");
        }
    }
}
