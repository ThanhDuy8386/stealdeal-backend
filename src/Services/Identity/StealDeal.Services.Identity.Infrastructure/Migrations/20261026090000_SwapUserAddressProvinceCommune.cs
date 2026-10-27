using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using StealDeal.Services.Identity.Infrastructure.Persistence;

#nullable disable

namespace StealDeal.Services.Identity.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261026090000_SwapUserAddressProvinceCommune")]
    public partial class SwapUserAddressProvinceCommune : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [UserAddresses]
                SET
                    [Province] = [Commune],
                    [Commune] = [Province];
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [UserAddresses]
                SET
                    [Province] = [Commune],
                    [Commune] = [Province];
                """);
        }
    }
}