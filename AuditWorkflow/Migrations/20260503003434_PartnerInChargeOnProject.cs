using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class PartnerInChargeOnProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PartnerInChargeId",
                table: "Projects",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_PartnerInChargeId",
                table: "Projects",
                column: "PartnerInChargeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Users_PartnerInChargeId",
                table: "Projects",
                column: "PartnerInChargeId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Users_PartnerInChargeId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_PartnerInChargeId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "PartnerInChargeId",
                table: "Projects");
        }
    }
}
