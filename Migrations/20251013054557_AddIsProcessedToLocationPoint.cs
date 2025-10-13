using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddIsProcessedToLocationPoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Accuracy",
                table: "LocationPoints");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "LocationPoints");

            migrationBuilder.AddColumn<bool>(
                name: "IsProcessed",
                table: "LocationPoints",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 13, 5, 45, 56, 650, DateTimeKind.Utc).AddTicks(91), "$2a$11$VtMYOJjDiLqPrpwsdBIdL.aAujuIsKYv.KGNOAlN/CmLRwqt5SCaO" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsProcessed",
                table: "LocationPoints");

            migrationBuilder.AddColumn<double>(
                name: "Accuracy",
                table: "LocationPoints",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "LocationPoints",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 13, 4, 43, 35, 293, DateTimeKind.Utc).AddTicks(4078), "$2a$11$YzptMy17vOrnjnEFpyCrS.ESsVhNpZRJxgTGzrxLNQqDO2RQLyE7." });
        }
    }
}
