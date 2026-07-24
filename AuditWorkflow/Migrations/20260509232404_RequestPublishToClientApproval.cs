using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class RequestPublishToClientApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PublishApprovalRequestedByUserId",
                table: "Requests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishApprovalRequestedUtc",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishToClientApprovalState",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Requests_PublishApprovalRequestedByUserId",
                table: "Requests",
                column: "PublishApprovalRequestedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_Users_PublishApprovalRequestedByUserId",
                table: "Requests",
                column: "PublishApprovalRequestedByUserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Requests_Users_PublishApprovalRequestedByUserId",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_PublishApprovalRequestedByUserId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "PublishApprovalRequestedByUserId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "PublishApprovalRequestedUtc",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "PublishToClientApprovalState",
                table: "Requests");
        }
    }
}
