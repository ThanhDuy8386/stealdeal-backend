using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StealDeal.Services.Store.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCategorySuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CategorySuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuggestedName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AdminComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategorySuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategorySuggestions_StoreProfiles_StoreId",
                        column: x => x.StoreId,
                        principalTable: "StoreProfiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategorySuggestions_Status",
                table: "CategorySuggestions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_CategorySuggestions_StoreId",
                table: "CategorySuggestions",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategorySuggestions");
        }
    }
}
