using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StealDeal.Services.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserRoleToRoleManyToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.Sql("""
                INSERT INTO Roles (Id, Name, CreatedAt)
                SELECT NEWID(), Role, GETUTCDATE()
                FROM UserRoles
                WHERE Role IS NOT NULL
                GROUP BY Role;
                """);

            migrationBuilder.Sql("""
                INSERT INTO Roles (Id, Name, CreatedAt)
                SELECT NEWID(), SeedRoles.Name, GETUTCDATE()
                FROM (VALUES ('Customer'), ('Seller'), ('Admin')) AS SeedRoles(Name)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM Roles
                    WHERE Roles.Name = SeedRoles.Name
                );
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Users_UserId",
                table: "UserRoles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "UserRoles",
                newName: "UsersId");

            migrationBuilder.RenameIndex(
                name: "IX_UserRoles_UserId",
                table: "UserRoles",
                newName: "IX_UserRoles_UsersId");

            migrationBuilder.AddColumn<Guid>(
                name: "RolesId",
                table: "UserRoles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE ur
                SET RolesId = r.Id
                FROM UserRoles ur
                INNER JOIN Roles r ON r.Name = ur.Role;
                """);

            migrationBuilder.Sql("""
                WITH DuplicateRoles AS
                (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER (
                            PARTITION BY UsersId, RolesId
                            ORDER BY AssignedAt, Id
                        ) AS RowNumber
                    FROM UserRoles
                )
                DELETE FROM DuplicateRoles
                WHERE RowNumber > 1;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "RolesId",
                table: "UserRoles",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "UserRoles");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "UserRoles");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "UserRoles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles",
                columns: new[] { "RolesId", "UsersId" });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Roles_RolesId",
                table: "UserRoles",
                column: "RolesId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Users_UsersId",
                table: "UserRoles",
                column: "UsersId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Roles_RolesId",
                table: "UserRoles");

            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Users_UsersId",
                table: "UserRoles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "UserRoles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.RenameColumn(
                name: "UsersId",
                table: "UserRoles",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "IX_UserRoles_UsersId",
                table: "UserRoles",
                newName: "IX_UserRoles_UserId");

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "UserRoles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "UserRoles",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE ur
                SET Role = r.Name
                FROM UserRoles ur
                INNER JOIN Roles r ON r.Id = ur.RolesId;
                """);

            migrationBuilder.DropColumn(
                name: "RolesId",
                table: "UserRoles");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserRoles",
                table: "UserRoles",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Users_UserId",
                table: "UserRoles",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
