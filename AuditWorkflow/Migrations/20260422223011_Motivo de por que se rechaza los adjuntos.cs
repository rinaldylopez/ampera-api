using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class Motivodeporqueserechazalosadjuntos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProfileItems_ProfileId",
                table: "ProfileItems");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileItems_ProfileId_NavigationItemId",
                table: "ProfileItems",
                columns: new[] { "ProfileId", "NavigationItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProfileItems_ProfileId_NavigationItemId",
                table: "ProfileItems");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileItems_ProfileId",
                table: "ProfileItems",
                column: "ProfileId");
        }
    }
}
