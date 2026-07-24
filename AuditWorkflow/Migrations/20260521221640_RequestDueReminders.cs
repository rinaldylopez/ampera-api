using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class RequestDueReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultReminderDaysBeforeDue",
                table: "Projects",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RequestReminders",
                columns: table => new
                {
                    RequestReminderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    DaysBeforeDue = table.Column<int>(type: "int", nullable: false),
                    NotifyFirmStaff = table.Column<bool>(type: "bit", nullable: false),
                    NotifyClientPortal = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestReminders", x => x.RequestReminderId);
                    table.ForeignKey(
                        name: "FK_RequestReminders_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestReminders_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RequestReminderDispatches",
                columns: table => new
                {
                    RequestReminderDispatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestReminderId = table.Column<int>(type: "int", nullable: false),
                    NotifyOnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestReminderDispatches", x => x.RequestReminderDispatchId);
                    table.ForeignKey(
                        name: "FK_RequestReminderDispatches_RequestReminders_RequestReminderId",
                        column: x => x.RequestReminderId,
                        principalTable: "RequestReminders",
                        principalColumn: "RequestReminderId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestReminderDispatches_RequestReminderId_NotifyOnDate",
                table: "RequestReminderDispatches",
                columns: new[] { "RequestReminderId", "NotifyOnDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestReminders_CreatedByUserId",
                table: "RequestReminders",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestReminders_RequestId",
                table: "RequestReminders",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestReminderDispatches");

            migrationBuilder.DropTable(
                name: "RequestReminders");

            migrationBuilder.DropColumn(
                name: "DefaultReminderDaysBeforeDue",
                table: "Projects");
        }
    }
}
