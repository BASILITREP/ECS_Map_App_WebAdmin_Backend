using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTripsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "Users",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "longblob")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "TripModelId",
                table: "LocationPoints",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Trips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FieldEngineerId = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    StartAddress = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EndAddress = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Distance = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Trips_FieldEngineers_FieldEngineerId",
                        column: x => x.FieldEngineerId,
                        principalTable: "FieldEngineers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 10, 6, 7, 32, 6, 10, DateTimeKind.Utc).AddTicks(5127), "$2a$11$L5gstD88JtD7QVlBiPiDeeFCY1DpRIWBv2bG5Wm0SWH/6qauxIcaa" });

            migrationBuilder.CreateIndex(
                name: "IX_LocationPoints_TripModelId",
                table: "LocationPoints",
                column: "TripModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_FieldEngineerId",
                table: "Trips",
                column: "FieldEngineerId");

            migrationBuilder.AddForeignKey(
                name: "FK_LocationPoints_Trips_TripModelId",
                table: "LocationPoints",
                column: "TripModelId",
                principalTable: "Trips",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LocationPoints_Trips_TripModelId",
                table: "LocationPoints");

            migrationBuilder.DropTable(
                name: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_LocationPoints_TripModelId",
                table: "LocationPoints");

            migrationBuilder.DropColumn(
                name: "TripModelId",
                table: "LocationPoints");

            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<byte[]>(
                name: "PasswordHash",
                table: "Users",
                type: "longblob",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "PasswordHash" },
                values: new object[] { new DateTime(2025, 9, 25, 6, 49, 59, 378, DateTimeKind.Utc).AddTicks(5673), new byte[] { 36, 50, 97, 36, 49, 49, 36, 82, 82, 97, 101, 100, 99, 106, 122, 114, 110, 56, 104, 47, 83, 49, 78, 109, 109, 122, 116, 99, 117, 74, 109, 65, 76, 88, 97, 78, 116, 76, 49, 114, 102, 82, 87, 103, 54, 120, 117, 115, 112, 103, 100, 102, 49, 49, 47, 106, 79, 108, 119, 117 } });
        }
    }
}
