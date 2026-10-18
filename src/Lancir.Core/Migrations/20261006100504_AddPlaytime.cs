using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaytime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PlaytimeSeconds",
                table: "Games",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlaytimeSeconds",
                table: "Games");
        }
    }
}
