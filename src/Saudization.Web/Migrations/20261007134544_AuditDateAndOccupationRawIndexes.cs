using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saudization.Web.Migrations
{
    /// <inheritdoc />
    public partial class AuditDateAndOccupationRawIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Employees_OccupationRaw",
                table: "Employees",
                column: "OccupationRaw");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ChangedAtUtc",
                table: "AuditLogs",
                column: "ChangedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Employees_OccupationRaw",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ChangedAtUtc",
                table: "AuditLogs");
        }
    }
}
