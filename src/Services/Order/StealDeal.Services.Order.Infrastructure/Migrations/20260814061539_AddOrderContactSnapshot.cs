using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StealDeal.Services.Order.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderContactSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactNameSnapshot",
                table: "OrderProfiles",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContactPhoneSnapshot",
                table: "OrderProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactNameSnapshot",
                table: "OrderProfiles");

            migrationBuilder.DropColumn(
                name: "ContactPhoneSnapshot",
                table: "OrderProfiles");
        }
    }
}
