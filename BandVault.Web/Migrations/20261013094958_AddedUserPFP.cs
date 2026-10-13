using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandVault.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddedUserPFP : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FanPrice",
                table: "MerchItems",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FanPrice",
                table: "MerchItems");
        }
    }
}
