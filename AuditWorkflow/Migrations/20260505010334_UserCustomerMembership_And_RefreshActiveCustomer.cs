using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class UserCustomerMembership_And_RefreshActiveCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveCustomerId",
                table: "UserRefreshTokens",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserCustomerMemberships",
                columns: table => new
                {
                    UserCustomerMembershipId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCustomerMemberships", x => x.UserCustomerMembershipId);
                    table.ForeignKey(
                        name: "FK_UserCustomerMemberships_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserCustomerMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_ActiveCustomerId",
                table: "UserRefreshTokens",
                column: "ActiveCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCustomerMemberships_CustomerId",
                table: "UserCustomerMemberships",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCustomerMemberships_UserId_CustomerId",
                table: "UserCustomerMemberships",
                columns: new[] { "UserId", "CustomerId" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO UserCustomerMemberships (UserId, CustomerId)
                SELECT u.UserId, u.CustomerId
                FROM Users AS u
                WHERE u.UserType = 2
                  AND u.CustomerId IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM UserCustomerMemberships AS m
                    WHERE m.UserId = u.UserId AND m.CustomerId = u.CustomerId)
                """);

            migrationBuilder.Sql("""
                UPDATE urt
                SET ActiveCustomerId = u.CustomerId
                FROM UserRefreshTokens AS urt
                INNER JOIN Users AS u ON u.UserId = urt.UserId
                WHERE u.UserType = 2 AND u.CustomerId IS NOT NULL
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRefreshTokens_Customers_ActiveCustomerId",
                table: "UserRefreshTokens",
                column: "ActiveCustomerId",
                principalTable: "Customers",
                principalColumn: "CustomerId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserRefreshTokens_Customers_ActiveCustomerId",
                table: "UserRefreshTokens");

            migrationBuilder.DropTable(
                name: "UserCustomerMemberships");

            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_ActiveCustomerId",
                table: "UserRefreshTokens");

            migrationBuilder.DropColumn(
                name: "ActiveCustomerId",
                table: "UserRefreshTokens");
        }
    }
}
