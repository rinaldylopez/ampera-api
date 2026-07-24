using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class ProyectoborrandorelacionconNavigationItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NavigationItems_NavigationProfiles_NavigationProfilesProfileId",
                table: "NavigationItems");

            migrationBuilder.DropIndex(
                name: "IX_NavigationItems_NavigationProfilesProfileId",
                table: "NavigationItems");

            migrationBuilder.DropColumn(
                name: "NavigationProfilesProfileId",
                table: "NavigationItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NavigationProfilesProfileId",
                table: "NavigationItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_NavigationItems_NavigationProfilesProfileId",
                table: "NavigationItems",
                column: "NavigationProfilesProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_NavigationItems_NavigationProfiles_NavigationProfilesProfileId",
                table: "NavigationItems",
                column: "NavigationProfilesProfileId",
                principalTable: "NavigationProfiles",
                principalColumn: "ProfileId");
        }
    }
}
