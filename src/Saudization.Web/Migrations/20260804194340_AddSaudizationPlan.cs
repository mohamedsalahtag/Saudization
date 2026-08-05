using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saudization.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSaudizationPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaudizationPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    SnapshotId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaudizationPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaudizationPlans_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaudizationPlanCells",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlanId = table.Column<int>(type: "int", nullable: false),
                    RecordId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    Saudi = table.Column<int>(type: "int", nullable: false),
                    NonSaudi = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaudizationPlanCells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaudizationPlanCells_SaudizationPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "SaudizationPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaudizationPlanCells_PlanId",
                table: "SaudizationPlanCells",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SaudizationPlans_SnapshotId",
                table: "SaudizationPlans",
                column: "SnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaudizationPlanCells");

            migrationBuilder.DropTable(
                name: "SaudizationPlans");
        }
    }
}
