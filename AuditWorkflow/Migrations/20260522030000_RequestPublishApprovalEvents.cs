using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class RequestPublishApprovalEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FirmInternalOnly",
                table: "RequestHistory",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RequestPublishApprovalEvents",
                columns: table => new
                {
                    RequestPublishApprovalEventId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    RelatedUserId = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestPublishApprovalEvents", x => x.RequestPublishApprovalEventId);
                    table.ForeignKey(
                        name: "FK_RequestPublishApprovalEvents_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "RequestId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RequestPublishApprovalEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RequestPublishApprovalEvents_Users_RelatedUserId",
                        column: x => x.RelatedUserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestPublishApprovalEvents_ActorUserId",
                table: "RequestPublishApprovalEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPublishApprovalEvents_RelatedUserId",
                table: "RequestPublishApprovalEvents",
                column: "RelatedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestPublishApprovalEvents_RequestId",
                table: "RequestPublishApprovalEvents",
                column: "RequestId");

            migrationBuilder.Sql("""
                UPDATE RequestHistory
                SET FirmInternalOnly = 1
                WHERE StatusAtMoment = N'Flujo portal'
                  AND (
                    Observation LIKE N'%aprobación%'
                    OR Observation LIKE N'%rechazó el envío%'
                    OR Observation LIKE N'%publicó la solicitud%'
                    OR Observation LIKE N'%Cancelación%envío%'
                    OR Observation LIKE N'%canceló el envío%'
                  );

                INSERT INTO RequestPublishApprovalEvents (RequestId, EventType, ActorUserId, RelatedUserId, Reason, OccurredAtUtc)
                SELECT RequestId, 3, RejectedByUserId, RequestedByUserId, Reason, RejectedAtUtc
                FROM RequestPublishApprovalRejections;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestPublishApprovalEvents");

            migrationBuilder.DropColumn(
                name: "FirmInternalOnly",
                table: "RequestHistory");
        }
    }
}
