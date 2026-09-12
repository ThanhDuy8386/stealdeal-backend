using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StealDeal.Services.Store.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewCountAndRepliedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StoreReviews_OrderId",
                table: "StoreReviews");

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "StoreReviews",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RepliedAt",
                table: "StoreReviews",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewCount",
                table: "StoreProfiles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_OrderId_BagId",
                table: "StoreReviews",
                columns: new[] { "OrderId", "BagId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StoreReviews_OrderId_BagId",
                table: "StoreReviews");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                table: "StoreReviews");

            migrationBuilder.DropColumn(
                name: "RepliedAt",
                table: "StoreReviews");

            migrationBuilder.DropColumn(
                name: "ReviewCount",
                table: "StoreProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_StoreReviews_OrderId",
                table: "StoreReviews",
                column: "OrderId",
                unique: true);
        }
    }
}
