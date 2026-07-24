using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class RequestPortalHandoffStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PortalHandoffStatus",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PortalHandoffStatus",
                table: "Requests");
        }
    }
}
