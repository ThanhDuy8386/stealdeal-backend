using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StealDeal.Services.Order.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPendingQuotaIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_OrderProfiles_UserId_Status",
                table: "OrderProfiles",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderProfiles_UserId_Status",
                table: "OrderProfiles");
        }
    }
}
