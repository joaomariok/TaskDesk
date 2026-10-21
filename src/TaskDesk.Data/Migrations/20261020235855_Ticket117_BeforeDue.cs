using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskDesk.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ticket117_BeforeDue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Amount",
                table: "Reminders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Unit",
                table: "Reminders",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Amount",
                table: "Reminders");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "Reminders");
        }
    }
}
