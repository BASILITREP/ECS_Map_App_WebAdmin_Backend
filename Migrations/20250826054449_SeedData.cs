using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EcsFeMappingApi.Migrations
{
    /// <inheritdoc />
    public partial class SeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seed Branches
            migrationBuilder.InsertData(
                table: "Branches",
                columns: new[] { "Id", "Name", "Address", "Latitude", "Longitude", "ContactPerson", "ContactNumber", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "BDO Alabang", "Lower Ground Floor, New Entertainment Complex, Alabang Town Center, No. 1139, Muntinlupa, 1780 Metro Manila", 14.4199, 121.0244, "Contact Person 1", "123-456-7890", DateTime.UtcNow, DateTime.UtcNow },
                    { 2, "BDO Makati", "G/F 6780 Bldg., Ayala Avenue, Makati City", 14.5547, 121.0244, "Contact Person 2", "123-456-7891", DateTime.UtcNow, DateTime.UtcNow },
                    { 3, "BDO Cebu IT Park", "G/F The Link, Cebu IT Park, Apas, Cebu City", 10.3157, 123.9054, "Contact Person 3", "123-456-7892", DateTime.UtcNow, DateTime.UtcNow },
                    { 4, "BDO Davao Bajada", "G/F Abreeza Mall, JP Laurel Ave, Davao City", 7.0714, 125.6127, "Contact Person 4", "123-456-7893", DateTime.UtcNow, DateTime.UtcNow },
                    { 5, "BDO BGC", "G/F One World Place, 32nd Street, Bonifacio Global City, Taguig", 14.5508, 121.0529, "Contact Person 5", "123-456-7894", DateTime.UtcNow, DateTime.UtcNow }
                });

            // Seed Field Engineers
            migrationBuilder.InsertData(
                table: "FieldEngineers",
                columns: new[] { "Id", "Name", "Email", "Phone", "CurrentLatitude", "CurrentLongitude", "IsAvailable", "Status", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "FE001 - Manila", "fe001@example.com", "987-654-3210", 14.5995, 120.9842, true, "Active", DateTime.UtcNow, DateTime.UtcNow },
                    { 2, "FE002 - Quezon City", "fe002@example.com", "987-654-3211", 14.6488, 120.9726, true, "Active", DateTime.UtcNow, DateTime.UtcNow },
                    { 3, "FE003 - Pasig", "fe003@example.com", "987-654-3212", 14.6057, 121.0509, false, "On Assignment", DateTime.UtcNow, DateTime.UtcNow },
                    { 4, "FE004 - Makati", "fe004@example.com", "987-654-3213", 14.5547, 121.0244, true, "Active", DateTime.UtcNow, DateTime.UtcNow },
                    { 5, "FE005 - Cebu", "fe005@example.com", "987-654-3214", 10.3157, 123.8854, true, "Active", DateTime.UtcNow, DateTime.UtcNow },
                    { 6, "FE006 - Davao", "fe006@example.com", "987-654-3215", 7.0714, 125.6127, false, "On Assignment", DateTime.UtcNow, DateTime.UtcNow }
                });

            // Seed Service Requests
            migrationBuilder.InsertData(
                table: "ServiceRequests",
                columns: new[] { "Id", "Title", "Description", "Status", "Priority", "BranchId", "FieldEngineerId", "CreatedAt", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, "ATM Maintenance", "Regular ATM maintenance required", "pending", "Medium", 1, null, DateTime.UtcNow, DateTime.UtcNow },
                    { 2, "Network Issue", "Branch experiencing network connectivity problems", "accepted", "High", 2, 4, DateTime.UtcNow, DateTime.UtcNow },
                    { 3, "Software Update", "Need to update branch banking software", "pending", "Low", 3, null, DateTime.UtcNow, DateTime.UtcNow },
                    { 4, "Hardware Replacement", "Replace faulty computer hardware", "accepted", "Medium", 4, 6, DateTime.UtcNow, DateTime.UtcNow },
                    { 5, "Security System Check", "Routine security system inspection", "pending", "High", 5, null, DateTime.UtcNow, DateTime.UtcNow }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove seeded data in reverse order
            migrationBuilder.DeleteData(table: "ServiceRequests", keyColumn: "Id", keyValue: 1);
            migrationBuilder.DeleteData(table: "ServiceRequests", keyColumn: "Id", keyValue: 2);
            migrationBuilder.DeleteData(table: "ServiceRequests", keyColumn: "Id", keyValue: 3);
            migrationBuilder.DeleteData(table: "ServiceRequests", keyColumn: "Id", keyValue: 4);
            migrationBuilder.DeleteData(table: "ServiceRequests", keyColumn: "Id", keyValue: 5);

            migrationBuilder.DeleteData(table: "FieldEngineers", keyColumn: "Id", keyValue: 1);
            migrationBuilder.DeleteData(table: "FieldEngineers", keyColumn: "Id", keyValue: 2);
            migrationBuilder.DeleteData(table: "FieldEngineers", keyColumn: "Id", keyValue: 3);
            migrationBuilder.DeleteData(table: "FieldEngineers", keyColumn: "Id", keyValue: 4);
            migrationBuilder.DeleteData(table: "FieldEngineers", keyColumn: "Id", keyValue: 5);
            migrationBuilder.DeleteData(table: "FieldEngineers", keyColumn: "Id", keyValue: 6);

            migrationBuilder.DeleteData(table: "Branches", keyColumn: "Id", keyValue: 1);
            migrationBuilder.DeleteData(table: "Branches", keyColumn: "Id", keyValue: 2);
            migrationBuilder.DeleteData(table: "Branches", keyColumn: "Id", keyValue: 3);
            migrationBuilder.DeleteData(table: "Branches", keyColumn: "Id", keyValue: 4);
            migrationBuilder.DeleteData(table: "Branches", keyColumn: "Id", keyValue: 5);
        }
    }
}
