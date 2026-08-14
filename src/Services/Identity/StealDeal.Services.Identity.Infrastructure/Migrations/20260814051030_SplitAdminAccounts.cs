using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StealDeal.Services.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SplitAdminAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "AdminId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Admins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AvatarUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsEmailVerified = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Admins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminRoles",
                columns: table => new
                {
                    AdminsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RolesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminRoles", x => new { x.AdminsId, x.RolesId });
                    table.ForeignKey(
                        name: "FK_AdminRoles_Admins_AdminsId",
                        column: x => x.AdminsId,
                        principalTable: "Admins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdminRoles_Roles_RolesId",
                        column: x => x.RolesId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                IF NOT EXISTS (
                    SELECT 1
                    FROM Roles
                    WHERE Name = 'SuperAdmin'
                )
                BEGIN
                    INSERT INTO Roles (Id, Name, CreatedAt)
                    VALUES ('7af1829c-f1bb-47a6-ae8d-fec7ec101f90', 'SuperAdmin', GETUTCDATE());
                END
                """);

            migrationBuilder.Sql("""
                INSERT INTO Admins (
                    Id,
                    Email,
                    PasswordHash,
                    Phone,
                    FullName,
                    AvatarUrl,
                    IsEmailVerified,
                    IsActive,
                    IsDeleted,
                    CreatedAt
                )
                SELECT
                    users.Id,
                    users.Email,
                    users.PasswordHash,
                    users.Phone,
                    users.FullName,
                    users.AvatarUrl,
                    users.IsEmailVerified,
                    users.IsActive,
                    users.IsDeleted,
                    users.CreatedAt
                FROM Users users
                WHERE EXISTS (
                    SELECT 1
                    FROM UserRoles userRoles
                    INNER JOIN Roles roles ON roles.Id = userRoles.RolesId
                    WHERE userRoles.UsersId = users.Id
                      AND roles.Name IN ('Admin', 'SuperAdmin')
                )
                  AND NOT EXISTS (
                      SELECT 1
                      FROM Admins admins
                      WHERE admins.Id = users.Id
                  );
                """);

            migrationBuilder.Sql("""
                INSERT INTO AdminRoles (AdminsId, RolesId)
                SELECT DISTINCT
                    userRoles.UsersId,
                    userRoles.RolesId
                FROM UserRoles userRoles
                INNER JOIN Roles roles ON roles.Id = userRoles.RolesId
                INNER JOIN Admins admins ON admins.Id = userRoles.UsersId
                WHERE roles.Name IN ('Admin', 'SuperAdmin')
                  AND NOT EXISTS (
                      SELECT 1
                      FROM AdminRoles adminRoles
                      WHERE adminRoles.AdminsId = userRoles.UsersId
                        AND adminRoles.RolesId = userRoles.RolesId
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE refreshTokens
                SET AdminId = refreshTokens.UserId,
                    UserId = NULL
                FROM RefreshTokens refreshTokens
                INNER JOIN Admins admins ON admins.Id = refreshTokens.UserId;
                """);

            migrationBuilder.Sql("""
                DELETE users
                FROM Users users
                INNER JOIN Admins admins ON admins.Id = users.Id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_AdminId",
                table: "RefreshTokens",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminRoles_RolesId",
                table: "AdminRoles",
                column: "RolesId");

            migrationBuilder.CreateIndex(
                name: "IX_Admins_Email",
                table: "Admins",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_Admins_AdminId",
                table: "RefreshTokens",
                column: "AdminId",
                principalTable: "Admins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_Admins_AdminId",
                table: "RefreshTokens");

            migrationBuilder.Sql("""
                INSERT INTO Users (
                    Id,
                    Email,
                    PasswordHash,
                    Phone,
                    FullName,
                    AvatarUrl,
                    IsEmailVerified,
                    IsActive,
                    IsDeleted,
                    CreatedAt
                )
                SELECT
                    admins.Id,
                    admins.Email,
                    admins.PasswordHash,
                    admins.Phone,
                    admins.FullName,
                    admins.AvatarUrl,
                    admins.IsEmailVerified,
                    admins.IsActive,
                    admins.IsDeleted,
                    admins.CreatedAt
                FROM Admins admins
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM Users users
                    WHERE users.Id = admins.Id
                       OR users.Email = admins.Email
                );
                """);

            migrationBuilder.Sql("""
                INSERT INTO UserRoles (RolesId, UsersId)
                SELECT
                    adminRoles.RolesId,
                    adminRoles.AdminsId
                FROM AdminRoles adminRoles
                INNER JOIN Users users ON users.Id = adminRoles.AdminsId
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM UserRoles userRoles
                    WHERE userRoles.RolesId = adminRoles.RolesId
                      AND userRoles.UsersId = adminRoles.AdminsId
                );
                """);

            migrationBuilder.Sql("""
                UPDATE refreshTokens
                SET UserId = refreshTokens.AdminId,
                    AdminId = NULL
                FROM RefreshTokens refreshTokens
                INNER JOIN Users users ON users.Id = refreshTokens.AdminId
                WHERE refreshTokens.UserId IS NULL;
                """);

            migrationBuilder.Sql("""
                DELETE FROM RefreshTokens
                WHERE UserId IS NULL;
                """);

            migrationBuilder.DropTable(
                name: "AdminRoles");

            migrationBuilder.DropTable(
                name: "Admins");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_AdminId",
                table: "RefreshTokens");

            migrationBuilder.Sql("""
                DELETE FROM Roles
                WHERE Id = '7af1829c-f1bb-47a6-ae8d-fec7ec101f90'
                  AND Name = 'SuperAdmin'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM UserRoles
                      WHERE UserRoles.RolesId = Roles.Id
                  );
                """);

            migrationBuilder.DropColumn(
                name: "AdminId",
                table: "RefreshTokens");

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
