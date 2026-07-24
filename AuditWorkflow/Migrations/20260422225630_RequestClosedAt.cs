using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class RequestClosedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClosedById",
                table: "Requests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_ClosedById",
                table: "Requests",
                column: "ClosedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Users_ClosedById",
                table: "Requests",
                column: "ClosedById",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Users_ClosedById",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_ClosedById",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "ClosedById",
                table: "Requests");
        }
    }
}
