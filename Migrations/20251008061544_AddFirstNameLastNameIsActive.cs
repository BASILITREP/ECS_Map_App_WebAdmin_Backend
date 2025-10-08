using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddFirstNameLastNameIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "FieldEngineers",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "FieldEngineers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "FieldEngineers",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 8, 6, 15, 42, 459, DateTimeKind.Utc).AddTicks(4658), "$2a$11$u9BX463tOA2ySNK4Ot2NE.AiYZlGVGDzD1Q8gGawKYg.dMFlJpmxC" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "FieldEngineers");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "FieldEngineers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "FieldEngineers");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 7, 8, 57, 53, 449, DateTimeKind.Utc).AddTicks(6999), "$2a$11$L4Yjuczpwa.AI0QEVK.lVuiGWQGG08Eb6CFG0qh1j7dww1YRXMmh2" });
        }
    }
}
