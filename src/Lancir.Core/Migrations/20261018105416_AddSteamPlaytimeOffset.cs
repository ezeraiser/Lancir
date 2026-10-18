using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamPlaytimeOffset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SteamPlaytimeOffsetSeconds",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SteamPlaytimeOffsetSeconds",
                table: "Games");
        }
    }
}
