using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskDesk.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ticket116_Reminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reminders_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_TaskId",
                table: "Reminders",
                column: "TaskId");

            // Each existing Reminder becomes one Fixed Reminder (Kind 0) with the same instant, so nothing about
            // what fires changes. A Task had at most one Reminder, so the Task's id is a unique id for it.
            migrationBuilder.Sql(
                """
                INSERT INTO "Reminders" ("Id", "TaskId", "Kind", "AtUtc")
                SELECT "Id", "Id", 0, "ReminderUtc" FROM "Tasks" WHERE "ReminderUtc" IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Tasks_ReminderUtc",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ReminderUtc",
                table: "Tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderUtc",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            // A Task keeps the Reminder that fires first.
            migrationBuilder.Sql(
                """
                UPDATE "Tasks" SET "ReminderUtc" =
                    (SELECT MIN("AtUtc") FROM "Reminders" WHERE "Reminders"."TaskId" = "Tasks"."Id" AND "Kind" = 0);
                """);

            migrationBuilder.DropTable(
                name: "Reminders");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ReminderUtc",
                table: "Tasks",
                column: "ReminderUtc");
        }
    }
}
