using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFareAndTravelTimeToActivityEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CalculatedFare",
                table: "ActivityEvents",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TravelTimeCategory",
                table: "ActivityEvents",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 25, 3, 4, 59, 222, DateTimeKind.Utc).AddTicks(5753), "$2a$11$EY/xnjLYElYaRZN0ePhSZ.4Wj0/ogboOlQv.Xk/hHZNRdMC5.mKEG" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalculatedFare",
                table: "ActivityEvents");

            migrationBuilder.DropColumn(
                name: "TravelTimeCategory",
                table: "ActivityEvents");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 22, 6, 55, 5, 187, DateTimeKind.Utc).AddTicks(9296), "$2a$11$XDMKwSmPJe0VATtKJCrx4.J84oYemE2WnvFELpvEOPwSXwqQtqrhq" });
        }
    }
}
