using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations;

/// <summary>
/// Ajoute uniquement les périodes pédagogiques. Les mensualités existantes
/// de la table Periodes et toutes les données ITM restent intactes.
/// </summary>
[DbContext(typeof(ScoliaDbContext))]
[Migration("20261111090000_PeriodesPedagogiques")]
public partial class PeriodesPedagogiques : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PeriodesPedagogiques",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Order = table.Column<int>(type: "INTEGER", nullable: false),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
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
            name: "IX_PeriodesPedagogiques_AcademicYearId_Code",
            table: "PeriodesPedagogiques",
            columns: new[] { "AcademicYearId", "Code" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PeriodesPedagogiques_AcademicYearId_Order",
            table: "PeriodesPedagogiques",
            columns: new[] { "AcademicYearId", "Order" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PeriodesPedagogiques");
    }
}
