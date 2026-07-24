using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class UserFirmMembership_And_RefreshActiveFirm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserFirmMemberships",
                columns: table => new
                {
                    UserFirmMembershipId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FirmId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFirmMemberships", x => x.UserFirmMembershipId);
                    table.ForeignKey(
                        name: "FK_UserFirmMemberships_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "FirmId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserFirmMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFirmMemberships_FirmId",
                table: "UserFirmMemberships",
                column: "FirmId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFirmMemberships_UserId_FirmId",
                table: "UserFirmMemberships",
                columns: new[] { "UserId", "FirmId" },
                unique: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveFirmId",
                table: "UserRefreshTokens",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE urt
                SET ActiveFirmId = u.FirmId
                FROM UserRefreshTokens AS urt
                INNER JOIN Users AS u ON u.UserId = urt.UserId
                WHERE urt.ActiveFirmId IS NULL
                """);

            migrationBuilder.Sql("""
                DELETE FROM UserRefreshTokens WHERE ActiveFirmId IS NULL
                """);

            migrationBuilder.AlterColumn<int>(
                name: "ActiveFirmId",
                table: "UserRefreshTokens",
                type: "int",
                nullable: false,
                oldClrType: typeof(int?),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.Sql("""
                INSERT INTO UserFirmMemberships (UserId, FirmId)
                SELECT u.UserId, u.FirmId
                FROM Users AS u
                WHERE u.UserType = 1
                  AND NOT EXISTS (
                    SELECT 1 FROM UserFirmMemberships AS m
                    WHERE m.UserId = u.UserId AND m.FirmId = u.FirmId)
                """);

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_ActiveFirmId",
                table: "UserRefreshTokens",
                column: "ActiveFirmId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserRefreshTokens_Firms_ActiveFirmId",
                table: "UserRefreshTokens",
                column: "ActiveFirmId",
                principalTable: "Firms",
                principalColumn: "FirmId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserRefreshTokens_Firms_ActiveFirmId",
                table: "UserRefreshTokens");

            migrationBuilder.DropTable(
                name: "UserFirmMemberships");

            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_ActiveFirmId",
                table: "UserRefreshTokens");

            migrationBuilder.DropColumn(
                name: "ActiveFirmId",
                table: "UserRefreshTokens");
        }
    }
}
