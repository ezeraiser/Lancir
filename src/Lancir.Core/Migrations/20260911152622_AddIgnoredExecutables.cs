using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lancir.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddIgnoredExecutables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IgnoredExecutables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExecutablePath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgnoredExecutables", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IgnoredExecutables_ExecutablePath",
                table: "IgnoredExecutables",
                column: "ExecutablePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IgnoredExecutables");
        }
    }
}
