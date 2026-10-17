using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskDesk.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ticket5_ListColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorHex",
                table: "Lists",
                type: "TEXT",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorHex",
                table: "Lists");
        }
    }
}
