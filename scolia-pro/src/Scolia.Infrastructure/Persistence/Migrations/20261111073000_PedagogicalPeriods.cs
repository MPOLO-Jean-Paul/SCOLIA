using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations;

/// <summary>Table isolée des périodes de cours : aucun changement aux échéances financières existantes.</summary>
[DbContext(typeof(ScoliaDbContext))]
[Migration("20261111073000_PedagogicalPeriods")]
public partial class PedagogicalPeriodsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PeriodesPedagogiques",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                Number = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PeriodesPedagogiques", x => x.Id);
                table.ForeignKey(
                    name: "FK_PeriodesPedagogiques_AnneesScolaires_AcademicYearId",
                    column: x => x.AcademicYearId,
                    principalTable: "AnneesScolaires",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PeriodesPedagogiques_AcademicYearId_Kind_Number",
            table: "PeriodesPedagogiques",
            columns: new[] { "AcademicYearId", "Kind", "Number" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PeriodesPedagogiques");
    }
}
