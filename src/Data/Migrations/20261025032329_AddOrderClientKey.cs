using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MenuToOrder.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderClientKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientKey",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RestaurantId_ClientKey",
                table: "Orders",
                columns: new[] { "RestaurantId", "ClientKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_RestaurantId_ClientKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ClientKey",
                table: "Orders");
        }
    }
}
