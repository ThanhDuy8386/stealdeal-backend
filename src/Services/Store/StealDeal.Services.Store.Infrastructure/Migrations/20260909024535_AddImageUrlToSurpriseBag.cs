using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StealDeal.Services.Store.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImageUrlToSurpriseBag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "SurpriseBags",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "SurpriseBags");
        }
    }
}
