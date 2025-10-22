using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 22, 5, 39, 48, 207, DateTimeKind.Utc).AddTicks(8841), "$2a$11$pNiFJg6atcSOVr5upJlsZOxGdl1jOBYPlmlQr/U8Nd4rwb.dxv7du" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 21, 8, 2, 6, 89, DateTimeKind.Utc).AddTicks(9657), "$2a$11$QtVBH5kVOtRp5yTosaGOledyPaNHTqBy3Fnf4t8aIKcuwh7458a3O" });
        }
    }
}
