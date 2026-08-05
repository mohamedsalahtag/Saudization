using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saudization.Web.Migrations
{
    /// <inheritdoc />
    public partial class OrgRestructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrgVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsOriginal = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceVersionId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgDivisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrgVersionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgDivisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgDivisions_OrgVersions_OrgVersionId",
                        column: x => x.OrgVersionId,
                        principalTable: "OrgVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrgDepartments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrgVersionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OrgDivisionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgDepartments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgDepartments_OrgDivisions_OrgDivisionId",
                        column: x => x.OrgDivisionId,
                        principalTable: "OrgDivisions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgDepartments_OrgVersions_OrgVersionId",
                        column: x => x.OrgVersionId,
                        principalTable: "OrgVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrgJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrgVersionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OrgDepartmentId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgJobs_OrgDepartments_OrgDepartmentId",
                        column: x => x.OrgDepartmentId,
                        principalTable: "OrgDepartments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgJobs_OrgVersions_OrgVersionId",
                        column: x => x.OrgVersionId,
                        principalTable: "OrgVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrgEmployees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrgVersionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    BranchCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Site = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    DirectManagerCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    OrgDepartmentId = table.Column<int>(type: "int", nullable: false),
                    OrgJobId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgEmployees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgEmployees_OrgDepartments_OrgDepartmentId",
                        column: x => x.OrgDepartmentId,
                        principalTable: "OrgDepartments",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgEmployees_OrgJobs_OrgJobId",
                        column: x => x.OrgJobId,
                        principalTable: "OrgJobs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrgEmployees_OrgVersions_OrgVersionId",
                        column: x => x.OrgVersionId,
                        principalTable: "OrgVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrgDepartments_OrgDivisionId",
                table: "OrgDepartments",
                column: "OrgDivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgDepartments_OrgVersionId",
                table: "OrgDepartments",
                column: "OrgVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgDivisions_OrgVersionId",
                table: "OrgDivisions",
                column: "OrgVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgEmployees_OrgDepartmentId",
                table: "OrgEmployees",
                column: "OrgDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgEmployees_OrgJobId",
                table: "OrgEmployees",
                column: "OrgJobId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgEmployees_OrgVersionId",
                table: "OrgEmployees",
                column: "OrgVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgEmployees_OrgVersionId_Code",
                table: "OrgEmployees",
                columns: new[] { "OrgVersionId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgJobs_OrgDepartmentId",
                table: "OrgJobs",
                column: "OrgDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgJobs_OrgVersionId",
                table: "OrgJobs",
                column: "OrgVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgEmployees");

            migrationBuilder.DropTable(
                name: "OrgJobs");

            migrationBuilder.DropTable(
                name: "OrgDepartments");

            migrationBuilder.DropTable(
                name: "OrgDivisions");

            migrationBuilder.DropTable(
                name: "OrgVersions");
        }
    }
}
