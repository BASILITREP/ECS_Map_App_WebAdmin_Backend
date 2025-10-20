using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRoutePathToActivityEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoutePathJson",
                table: "ActivityEvents",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 20, 3, 14, 44, 932, DateTimeKind.Utc).AddTicks(2517), "$2a$11$UKtPOBHSFoI32mluFoVbVemXevAdfAg18zUkpcXWLw3G2AgY95rwm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoutePathJson",
                table: "ActivityEvents");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 13, 5, 45, 56, 650, DateTimeKind.Utc).AddTicks(91), "$2a$11$VtMYOJjDiLqPrpwsdBIdL.aAujuIsKYv.KGNOAlN/CmLRwqt5SCaO" });
        }
    }
}
