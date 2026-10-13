using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saudization.Web.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotSoftDeleteAndRestrictFks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Facilities_FacilityId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Facilities_Records_RecordId",
                table: "Facilities");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Snapshots",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Snapshots",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUser",
                table: "Snapshots",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Facilities_FacilityId",
                table: "Employees",
                column: "FacilityId",
                principalTable: "Facilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Facilities_Records_RecordId",
                table: "Facilities",
                column: "RecordId",
                principalTable: "Records",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Employees_Facilities_FacilityId",
                table: "Employees");

            migrationBuilder.DropForeignKey(
                name: "FK_Facilities_Records_RecordId",
                table: "Facilities");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "DeletedByUser",
                table: "Snapshots");

            migrationBuilder.AddForeignKey(
                name: "FK_Employees_Facilities_FacilityId",
                table: "Employees",
                column: "FacilityId",
                principalTable: "Facilities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Facilities_Records_RecordId",
                table: "Facilities",
                column: "RecordId",
                principalTable: "Records",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
