using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saudization.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgEmployeeNationality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "OrgEmployees",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "OrgEmployees");
        }
    }
}
