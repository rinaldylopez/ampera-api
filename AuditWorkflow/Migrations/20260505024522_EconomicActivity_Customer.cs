using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class EconomicActivity_Customer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EconomicActivityId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EconomicActivities",
                columns: table => new
                {
                    EconomicActivityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EconomicActivities", x => x.EconomicActivityId);
                });

            migrationBuilder.InsertData(
                table: "EconomicActivities",
                columns: new[] { "Code", "Name" },
                values: new object[,]
                {
                    { "0111", "Cultivo de cereales (excepto arroz)" },
                    { "4610", "Venta al por mayor a cambio de una retribución o por contrata" },
                    { "6201", "Actividades de programación informática" },
                    { "4632", "Comercio al por mayor de alimentos, bebidas y tabaco" }
                });

            migrationBuilder.Sql("""
                DECLARE @moduleId int = (SELECT TOP 1 ModuleId FROM Modules WHERE Name = N'AMPERA' ORDER BY ModuleId);

                IF @moduleId IS NOT NULL
                   AND NOT EXISTS (SELECT 1 FROM NavigationItems WHERE Path = N'/economic-activities')
                BEGIN
                    INSERT INTO NavigationItems (ModuleId, Name, Path)
                    VALUES (@moduleId, N'Actividades económicas', N'/economic-activities');
                END

                DECLARE @navId int = (SELECT TOP 1 NavigationItemId FROM NavigationItems WHERE Path = N'/economic-activities');

                IF @navId IS NOT NULL
                BEGIN
                    INSERT INTO ProfileItems (ProfileId, NavigationItemId)
                    SELECT DISTINCT pi.ProfileId, @navId
                    FROM ProfileItems pi
                    INNER JOIN NavigationItems ni ON ni.NavigationItemId = pi.NavigationItemId
                    WHERE ni.Path = N'/customers'
                      AND NOT EXISTS (
                          SELECT 1
                          FROM ProfileItems existing
                          WHERE existing.ProfileId = pi.ProfileId
                            AND existing.NavigationItemId = @navId
                      );
                END
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_EconomicActivityId",
                table: "Customers",
                column: "EconomicActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_EconomicActivities_Code",
                table: "EconomicActivities",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_EconomicActivities_EconomicActivityId",
                table: "Customers",
                column: "EconomicActivityId",
                principalTable: "EconomicActivities",
                principalColumn: "EconomicActivityId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_EconomicActivities_EconomicActivityId",
                table: "Customers");

            migrationBuilder.Sql("""
                DECLARE @navId int = (SELECT TOP 1 NavigationItemId FROM NavigationItems WHERE Path = N'/economic-activities');
                IF @navId IS NOT NULL
                BEGIN
                    DELETE FROM ProfileItems WHERE NavigationItemId = @navId;
                    DELETE FROM NavigationItems WHERE NavigationItemId = @navId;
                END
                """);

            migrationBuilder.DropTable(
                name: "EconomicActivities");

            migrationBuilder.DropIndex(
                name: "IX_Customers_EconomicActivityId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EconomicActivityId",
                table: "Customers");
        }
    }
}
