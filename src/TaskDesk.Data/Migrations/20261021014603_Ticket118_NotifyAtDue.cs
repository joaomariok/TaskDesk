using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskDesk.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ticket118_NotifyAtDue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // "Notify at Due" is on for every existing Task. The Dues already past are counted as notified after the
            // migration, in memory (DatabaseInitializer): a Due moment depends on the time zone and the default
            // Reminder time, which SQL can't read.
            migrationBuilder.AddColumn<bool>(
                name: "NotifyAtDue",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifyAtDue",
                table: "Tasks");
        }
    }
}
