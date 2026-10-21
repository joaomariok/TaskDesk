using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskDesk.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ticket119_Nag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NagPeriod",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NagPeriod",
                table: "Tasks");
        }
    }
}
