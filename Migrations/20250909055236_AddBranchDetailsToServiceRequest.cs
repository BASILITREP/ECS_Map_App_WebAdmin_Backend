using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchDetailsToServiceRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcceptedAt",
                table: "ServiceRequests",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BranchName",
                table: "ServiceRequests",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<double>(
                name: "CurrentRadiusKm",
                table: "ServiceRequests",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Lat",
                table: "ServiceRequests",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Lng",
                table: "ServiceRequests",
                type: "double",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "BranchName",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "CurrentRadiusKm",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "Lat",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "Lng",
                table: "ServiceRequests");
        }
    }
}
