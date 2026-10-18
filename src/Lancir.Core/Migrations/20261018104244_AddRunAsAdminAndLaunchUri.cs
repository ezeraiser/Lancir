using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddRunAsAdminAndLaunchUri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LaunchUri",
                table: "Games",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RunAsAdmin",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LaunchUri",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "RunAsAdmin",
                table: "Games");
        }
    }
}
