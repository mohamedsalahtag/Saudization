using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saudization.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddDisabilityDefaultWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisabilityDefaultWeight",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 4);   // backfill the existing singleton Settings row to the Nitaqat default
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisabilityDefaultWeight",
                table: "Settings");
        }
    }
}
