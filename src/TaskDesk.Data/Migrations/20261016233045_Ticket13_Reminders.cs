using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskDesk.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ticket13_Reminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderFiredUtc",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderUtc",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SnoozedUntilUtc",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ReminderUtc",
                table: "Tasks",
                column: "ReminderUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_ReminderUtc",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ReminderFiredUtc",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ReminderUtc",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SnoozedUntilUtc",
                table: "Tasks");
        }
    }
}
