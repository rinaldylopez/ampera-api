using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditWorkflow.Migrations
{
    /// <inheritdoc />
    public partial class CustomerPhonePrefixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PhoneCountryId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhoneCountryId",
                table: "CustomerContacts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhonePrefix",
                table: "Countries",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE Countries
                SET PhonePrefix = '+503'
                WHERE IsoCode = 'SV' OR Name = 'El Salvador'
                """);

            migrationBuilder.Sql("""
                UPDATE Customers
                SET PhoneCountryId = CountryId
                WHERE CountryId IS NOT NULL
                """);

            migrationBuilder.Sql("""
                UPDATE cc
                SET PhoneCountryId = c.PhoneCountryId
                FROM CustomerContacts AS cc
                INNER JOIN Customers AS c ON c.CustomerId = cc.CustomerId
                WHERE c.PhoneCountryId IS NOT NULL
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PhoneCountryId",
                table: "Customers",
                column: "PhoneCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerContacts_PhoneCountryId",
                table: "CustomerContacts",
                column: "PhoneCountryId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerContacts_Countries_PhoneCountryId",
                table: "CustomerContacts",
                column: "PhoneCountryId",
                principalTable: "Countries",
                principalColumn: "CountryId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Countries_PhoneCountryId",
                table: "Customers",
                column: "PhoneCountryId",
                principalTable: "Countries",
                principalColumn: "CountryId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerContacts_Countries_PhoneCountryId",
                table: "CustomerContacts");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Countries_PhoneCountryId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_PhoneCountryId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CustomerContacts_PhoneCountryId",
                table: "CustomerContacts");

            migrationBuilder.DropColumn(
                name: "PhoneCountryId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PhoneCountryId",
                table: "CustomerContacts");

            migrationBuilder.DropColumn(
                name: "PhonePrefix",
                table: "Countries");
        }
    }
}
