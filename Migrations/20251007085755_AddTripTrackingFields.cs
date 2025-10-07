using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTripTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "EndLatitude",
                table: "Trips",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "EndLocation",
                table: "Trips",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "EndLongitude",
                table: "Trips",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "StartLatitude",
                table: "Trips",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "StartLocation",
                table: "Trips",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "StartLongitude",
                table: "Trips",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TotalDistance",
                table: "Trips",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "TripType",
                table: "Trips",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

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
                values: new object[] { new DateTime(2025, 10, 7, 8, 57, 53, 449, DateTimeKind.Utc).AddTicks(6999), "$2a$11$L4Yjuczpwa.AI0QEVK.lVuiGWQGG08Eb6CFG0qh1j7dww1YRXMmh2" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndLatitude",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "EndLocation",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "EndLongitude",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "StartLatitude",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "StartLocation",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "StartLongitude",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TotalDistance",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "TripType",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "Accuracy",
                table: "LocationPoints");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "LocationPoints");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 6, 7, 32, 6, 10, DateTimeKind.Utc).AddTicks(5127), "$2a$11$L5gstD88JtD7QVlBiPiDeeFCY1DpRIWBv2bG5Wm0SWH/6qauxIcaa" });
        }
    }
}
