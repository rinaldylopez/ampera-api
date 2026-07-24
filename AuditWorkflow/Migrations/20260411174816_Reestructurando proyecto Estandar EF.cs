using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class ReestructurandoproyectoEstandarEF : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NavigationItems_Moduls_ModulId",
                table: "NavigationItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_NavigationProfiles_ProfileId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Moduls");

            migrationBuilder.DropTable(
                name: "NavigationProfilesItems");

            migrationBuilder.DropTable(
                name: "NavigationProfiles");

            migrationBuilder.DropColumn(
                name: "RolSecurity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UserPassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UserUser",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Link",
                table: "NavigationItems");

            migrationBuilder.RenameColumn(
                name: "UserEmail",
                table: "Users",
                newName: "SecurityRole");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "Users",
                newName: "FirmId");

            migrationBuilder.RenameIndex(
                name: "IX_Users_CompanyId",
                table: "Users",
                newName: "IX_Users_FirmId");

            migrationBuilder.RenameColumn(
                name: "ModulId",
                table: "NavigationItems",
                newName: "ModuleId");

            migrationBuilder.RenameColumn(
                name: "NavigationId",
                table: "NavigationItems",
                newName: "NavigationItemId");

            migrationBuilder.RenameIndex(
                name: "IX_NavigationItems_ModulId",
                table: "NavigationItems",
                newName: "IX_NavigationItems_ModuleId");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Path",
                table: "NavigationItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Firms",
                columns: table => new
                {
                    FirmId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaxId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Firms", x => x.FirmId);
                });

            migrationBuilder.CreateTable(
                name: "Modules",
                columns: table => new
                {
                    ModuleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modules", x => x.ModuleId);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    ProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.ProfileId);
                });

            migrationBuilder.CreateTable(
                name: "ProfileItems",
                columns: table => new
                {
                    ProfileItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    NavigationItemId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileItems", x => x.ProfileItemId);
                    table.ForeignKey(
                        name: "FK_ProfileItems_NavigationItems_NavigationItemId",
                        column: x => x.NavigationItemId,
                        principalTable: "NavigationItems",
                        principalColumn: "NavigationItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileItems_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "ProfileId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileItems_NavigationItemId",
                table: "ProfileItems",
                column: "NavigationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileItems_ProfileId",
                table: "ProfileItems",
                column: "ProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_NavigationItems_Modules_ModuleId",
                table: "NavigationItems",
                column: "ModuleId",
                principalTable: "Modules",
                principalColumn: "ModuleId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Firms_FirmId",
                table: "Users",
                column: "FirmId",
                principalTable: "Firms",
                principalColumn: "FirmId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Profiles_ProfileId",
                table: "Users",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "ProfileId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NavigationItems_Modules_ModuleId",
                table: "NavigationItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Firms_FirmId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Profiles_ProfileId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "Firms");

            migrationBuilder.DropTable(
                name: "Modules");

            migrationBuilder.DropTable(
                name: "ProfileItems");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Path",
                table: "NavigationItems");

            migrationBuilder.RenameColumn(
                name: "SecurityRole",
                table: "Users",
                newName: "UserEmail");

            migrationBuilder.RenameColumn(
                name: "FirmId",
                table: "Users",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_Users_FirmId",
                table: "Users",
                newName: "IX_Users_CompanyId");

            migrationBuilder.RenameColumn(
                name: "ModuleId",
                table: "NavigationItems",
                newName: "ModulId");

            migrationBuilder.RenameColumn(
                name: "NavigationItemId",
                table: "NavigationItems",
                newName: "NavigationId");

            migrationBuilder.RenameIndex(
                name: "IX_NavigationItems_ModuleId",
                table: "NavigationItems",
                newName: "IX_NavigationItems_ModulId");

            migrationBuilder.AddColumn<string>(
                name: "RolSecurity",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserPassword",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserUser",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Link",
                table: "NavigationItems",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    CompanyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyDocument = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.CompanyId);
                });

            migrationBuilder.CreateTable(
                name: "Moduls",
                columns: table => new
                {
                    ModulId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Icon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Moduls", x => x.ModulId);
                });

            migrationBuilder.CreateTable(
                name: "NavigationProfiles",
                columns: table => new
                {
                    ProfileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NavigationProfiles", x => x.ProfileId);
                });

            migrationBuilder.CreateTable(
                name: "NavigationProfilesItems",
                columns: table => new
                {
                    NavigationItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NavigationId = table.Column<int>(type: "int", nullable: false),
                    ProfileId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NavigationProfilesItems", x => x.NavigationItemId);
                    table.ForeignKey(
                        name: "FK_NavigationProfilesItems_NavigationItems_NavigationId",
                        column: x => x.NavigationId,
                        principalTable: "NavigationItems",
                        principalColumn: "NavigationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NavigationProfilesItems_NavigationProfiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "NavigationProfiles",
                        principalColumn: "ProfileId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NavigationProfilesItems_NavigationId",
                table: "NavigationProfilesItems",
                column: "NavigationId");

            migrationBuilder.CreateIndex(
                name: "IX_NavigationProfilesItems_ProfileId",
                table: "NavigationProfilesItems",
                column: "ProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_NavigationItems_Moduls_ModulId",
                table: "NavigationItems",
                column: "ModulId",
                principalTable: "Moduls",
                principalColumn: "ModulId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Companies_CompanyId",
                table: "Users",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "CompanyId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_NavigationProfiles_ProfileId",
                table: "Users",
                column: "ProfileId",
                principalTable: "NavigationProfiles",
                principalColumn: "ProfileId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
