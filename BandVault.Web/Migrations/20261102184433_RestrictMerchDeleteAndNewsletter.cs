using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BandVault.Web.Migrations
{
    /// <inheritdoc />
    public partial class RestrictMerchDeleteAndNewsletter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_MerchItems_MerchItemId",
                table: "OrderItems");

            migrationBuilder.CreateTable(
                name: "NewsletterSubscribers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsletterSubscribers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsletterSubscribers_Email",
                table: "NewsletterSubscribers",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_MerchItems_MerchItemId",
                table: "OrderItems",
                column: "MerchItemId",
                principalTable: "MerchItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_MerchItems_MerchItemId",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "NewsletterSubscribers");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_MerchItems_MerchItemId",
                table: "OrderItems",
                column: "MerchItemId",
                principalTable: "MerchItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
