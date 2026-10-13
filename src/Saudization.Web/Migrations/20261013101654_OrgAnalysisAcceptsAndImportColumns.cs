using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saudization.Web.Migrations
{
    /// <inheritdoc />
    public partial class OrgAnalysisAcceptsAndImportColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DepartmentHeads_Division_Department",
                table: "DepartmentHeads");

            migrationBuilder.AddColumn<string>(
                name: "DirectManagerName",
                table: "OrgEmployees",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Grade",
                table: "OrgEmployees",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrgVersionId",
                table: "DepartmentHeads",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrgFindingAccepts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FindingId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RowLabel = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    AcceptedByUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgFindingAccepts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentHeads_OrgVersionId_Division_Department",
                table: "DepartmentHeads",
                columns: new[] { "OrgVersionId", "Division", "Department" },
                unique: true,
                filter: "[OrgVersionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrgFindingAccepts_FindingId_RowKey",
                table: "OrgFindingAccepts",
                columns: new[] { "FindingId", "RowKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgFindingAccepts");

            migrationBuilder.DropIndex(
                name: "IX_DepartmentHeads_OrgVersionId_Division_Department",
                table: "DepartmentHeads");

            migrationBuilder.DropColumn(
                name: "DirectManagerName",
                table: "OrgEmployees");

            migrationBuilder.DropColumn(
                name: "Grade",
                table: "OrgEmployees");

            migrationBuilder.DropColumn(
                name: "OrgVersionId",
                table: "DepartmentHeads");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentHeads_Division_Department",
                table: "DepartmentHeads",
                columns: new[] { "Division", "Department" },
                unique: true);
        }
    }
}
