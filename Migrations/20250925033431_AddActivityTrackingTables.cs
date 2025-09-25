using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    public partial class AddActivityTrackingTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ensure no NULL values exist before altering the columns
            migrationBuilder.Sql("UPDATE FieldEngineers SET CurrentLongitude = 0.0 WHERE CurrentLongitude IS NULL;");
            migrationBuilder.Sql("UPDATE FieldEngineers SET CurrentLatitude = 0.0 WHERE CurrentLatitude IS NULL;");

            migrationBuilder.AlterColumn<double>(
                name: "CurrentLongitude",
                table: "FieldEngineers",
                type: "double",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "CurrentLatitude",
                table: "FieldEngineers",
                type: "double",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FcmToken",
                table: "FieldEngineers",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ActivityEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FieldEngineerId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    DistanceKm = table.Column<double>(type: "double", nullable: true),
                    TopSpeedKmh = table.Column<double>(type: "double", nullable: true),
                    LocationName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Address = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Latitude = table.Column<double>(type: "double", nullable: true),
                    Longitude = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEvents", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LocationPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FieldEngineerId = table.Column<int>(type: "int", nullable: false),
                    Latitude = table.Column<double>(type: "double", nullable: false),
                    Longitude = table.Column<double>(type: "double", nullable: false),
                    Speed = table.Column<double>(type: "double", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationPoints", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 9, 25, 3, 34, 29, 357, DateTimeKind.Utc).AddTicks(4042), new byte[] { 36, 50, 97, 36, 49, 49, 36, 80, 117, 71, 77, 82, 114, 116, 115, 71, 107, 53, 55, 105, 75, 101, 88, 86, 56, 103, 117, 98, 117, 121, 110, 101, 117, 70, 100, 68, 112, 50, 78, 116, 113, 89, 104, 48, 90, 67, 87, 66, 108, 99, 97, 117, 112, 48, 47, 79, 55, 78, 81, 46 } });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEvents");

            migrationBuilder.DropTable(
                name: "LocationPoints");

            migrationBuilder.DropColumn(
                name: "FcmToken",
                table: "FieldEngineers");

            migrationBuilder.AlterColumn<double>(
                name: "CurrentLongitude",
                table: "FieldEngineers",
                type: "double",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double");

            migrationBuilder.AlterColumn<double>(
                name: "CurrentLatitude",
                table: "FieldEngineers",
                type: "double",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 9, 15, 8, 55, 36, 285, DateTimeKind.Utc).AddTicks(1463), new byte[] { 36, 50, 97, 36, 49, 49, 36, 114, 108, 104, 71, 56, 53, 106, 83, 114, 70, 106, 56, 66, 57, 109, 79, 57, 89, 83, 109, 48, 101, 54, 110, 53, 111, 57, 68, 78, 54, 90, 119, 117, 82, 68, 107, 67, 117, 115, 110, 49, 89, 101, 107, 116, 53, 122, 50, 73, 68, 118, 48, 46 } });
        }
    }
}
