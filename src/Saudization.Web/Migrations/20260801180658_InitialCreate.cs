using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Saudization.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedByUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MinForCalc = table.Column<int>(type: "int", nullable: false),
                    CurrentRequiredPct = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    CurrentDecisionDate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FutureRequiredPct = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    FutureDecisionDate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MinWage = table.Column<decimal>(type: "decimal(10,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DisabilityRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CountAs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisabilityRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nationalities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsSaudi = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nationalities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Records",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegistrationLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    NameEn = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RequiredPct = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    LowGreenMin = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    MediumGreenMin = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    HighGreenMin = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    PlatinumMin = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    RedColor = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    LowGreenColor = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    MediumGreenColor = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    HighGreenColor = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    PlatinumColor = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DisabilityCapPct = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    LightGreenBand = table.Column<decimal>(type: "decimal(5,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    WeekDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUser = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SpecialCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IdNumber = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Occupation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Occupations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Occupations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Occupations_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Facilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    RecordId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Facilities_Records_RecordId",
                        column: x => x.RecordId,
                        principalTable: "Records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SnapshotId = table.Column<int>(type: "int", nullable: false),
                    WorkerNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NationalityId = table.Column<int>(type: "int", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    FacilityId = table.Column<int>(type: "int", nullable: false),
                    BorderNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    IqamaNo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    OccupationId = table.Column<int>(type: "int", nullable: true),
                    OccupationRaw = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IqamaExpiry = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    EntryDate = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    WorkerType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CountsAsSaudiSpecial = table.Column<bool>(type: "bit", nullable: false),
                    SpecialCategory = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    DisabilityCountAs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Employees_Nationalities_NationalityId",
                        column: x => x.NationalityId,
                        principalTable: "Nationalities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Employees_Occupations_OccupationId",
                        column: x => x.OccupationId,
                        principalTable: "Occupations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Employees_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityName_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_NameAr",
                table: "Categories",
                column: "NameAr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_FacilityId",
                table: "Employees",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_IqamaNo",
                table: "Employees",
                column: "IqamaNo");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_NationalityId",
                table: "Employees",
                column: "NationalityId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_OccupationId",
                table: "Employees",
                column: "OccupationId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_SnapshotId",
                table: "Employees",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_WorkerNo",
                table: "Employees",
                column: "WorkerNo");

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_Code",
                table: "Facilities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_RecordId",
                table: "Facilities",
                column: "RecordId");

            migrationBuilder.CreateIndex(
                name: "IX_Nationalities_NameAr",
                table: "Nationalities",
                column: "NameAr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Occupations_CategoryId",
                table: "Occupations",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Occupations_NameAr",
                table: "Occupations",
                column: "NameAr");

            migrationBuilder.CreateIndex(
                name: "IX_Records_RegistrationLabel",
                table: "Records",
                column: "RegistrationLabel",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_WeekDate",
                table: "Snapshots",
                column: "WeekDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DisabilityRecords");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "SpecialCases");

            migrationBuilder.DropTable(
                name: "Facilities");

            migrationBuilder.DropTable(
                name: "Nationalities");

            migrationBuilder.DropTable(
                name: "Occupations");

            migrationBuilder.DropTable(
                name: "Snapshots");

            migrationBuilder.DropTable(
                name: "Records");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
