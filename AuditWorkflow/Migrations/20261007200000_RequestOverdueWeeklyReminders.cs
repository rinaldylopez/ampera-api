using System;
using AuditWorkflow.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261007200000_RequestOverdueWeeklyReminders")]
    public class RequestOverdueWeeklyReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestOverdueReminderDispatches",
                columns: table => new
                {
                    RequestOverdueReminderDispatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    NotifyOnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestOverdueReminderDispatches", x => x.RequestOverdueReminderDispatchId);
                    table.ForeignKey(
                        name: "FK_RequestOverdueReminderDispatches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestOverdueReminderDispatches_UserId_NotifyOnDate",
                table: "RequestOverdueReminderDispatches",
                columns: new[] { "UserId", "NotifyOnDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RequestOverdueReminderDispatches");
        }
    }
}
