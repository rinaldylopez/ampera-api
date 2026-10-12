using System;
using AuditWorkflow.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261007190000_RequestResponseReceivedAt")]
    public class RequestResponseReceivedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResponseReceivedAt",
                table: "Requests",
                type: "datetime2",
                nullable: true);

            // Backfill: última vez que el cliente envió documentación a la firma (historial).
            migrationBuilder.Sql("""
                UPDATE r
                SET ResponseReceivedAt = h.LatestAt
                FROM Requests AS r
                INNER JOIN (
                    SELECT RequestId, MAX(ActionDate) AS LatestAt
                    FROM RequestHistory
                    WHERE StatusAtMoment = N'Flujo portal'
                      AND Observation LIKE N'%envió la documentación a revisión%'
                    GROUP BY RequestId
                ) AS h ON h.RequestId = r.RequestId
                WHERE r.ResponseReceivedAt IS NULL
                  AND r.PortalHandoffStatus = 2;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseReceivedAt",
                table: "Requests");
        }
    }
}
