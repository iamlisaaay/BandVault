using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BandVault.Web.Migrations
{
    /// <inheritdoc />
    public partial class SeedMerchCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MerchCategories",
                columns: new[] { "Id", "Name" },
                values: new object[,]
                {
                    { 1, "Одяг" },
                    { 2, "Аксесуари" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "MerchCategories",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "MerchCategories",
                keyColumn: "Id",
                keyValue: 2);
        }
    }
}
