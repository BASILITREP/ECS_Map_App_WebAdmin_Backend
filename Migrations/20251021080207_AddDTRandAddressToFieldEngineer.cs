using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDTRandAddressToFieldEngineer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentAddress",
                table: "FieldEngineers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "TimeIn",
                table: "FieldEngineers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 21, 8, 2, 6, 89, DateTimeKind.Utc).AddTicks(9657), "$2a$11$QtVBH5kVOtRp5yTosaGOledyPaNHTqBy3Fnf4t8aIKcuwh7458a3O" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentAddress",
                table: "FieldEngineers");

            migrationBuilder.DropColumn(
                name: "TimeIn",
                table: "FieldEngineers");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 20, 3, 14, 44, 932, DateTimeKind.Utc).AddTicks(2517), "$2a$11$UKtPOBHSFoI32mluFoVbVemXevAdfAg18zUkpcXWLw3G2AgY95rwm" });
        }
    }
}
