using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StealDeal.Services.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameUserAddressFieldsAndAddCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "District",
                table: "UserAddresses",
                newName: "Province");

            migrationBuilder.RenameColumn(
                name: "City",
                table: "UserAddresses",
                newName: "Commune");

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "UserAddresses",
                type: "decimal(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "UserAddresses",
                type: "decimal(10,7)",
                precision: 10,
                scale: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "UserAddresses");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "UserAddresses");

            migrationBuilder.RenameColumn(
                name: "Province",
                table: "UserAddresses",
                newName: "District");

            migrationBuilder.RenameColumn(
                name: "Commune",
                table: "UserAddresses",
                newName: "City");
        }
    }
}
