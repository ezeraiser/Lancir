using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamGridDbId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SteamGridDbId",
                table: "Games",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SteamGridDbId",
                table: "Games");
        }
    }
}
