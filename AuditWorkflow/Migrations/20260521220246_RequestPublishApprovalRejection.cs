using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class RequestPublishApprovalRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestPublishApprovalRejections",
                columns: table => new
                {
                    RequestPublishApprovalRejectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPublishApprovalRejections", x => x.RequestPublishApprovalRejectionId);
                    table.ForeignKey(
                        name: "FK_RequestPublishApprovalRejections_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestPublishApprovalRejections_Users_RejectedByUserId",
                        column: x => x.RejectedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestPublishApprovalRejections_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestPublishApprovalRejections_RejectedByUserId",
                table: "RequestPublishApprovalRejections",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPublishApprovalRejections_RequestedByUserId",
                table: "RequestPublishApprovalRejections",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPublishApprovalRejections_RequestId",
                table: "RequestPublishApprovalRejections",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestPublishApprovalRejections");
        }
    }
}
