using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations;

/// <summary>
/// Migration additive : aucun changement de notes, montants, reçus ni
/// inscriptions ITM. Les anciennes promotions conservent SchoolCycleId = NULL.
/// </summary>
[DbContext(typeof(ScoliaDbContext))]
[Migration("20261111140000_ComplexesScolairesPrives")]
public partial class ComplexesScolairesPrives : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Governance",
            table: "Etablissement",
            type: "INTEGER",
            nullable: false,
            defaultValue: 1); // Prive, compatible avec installations historiques.

        migrationBuilder.AddColumn<Guid>(
            name: "SchoolCycleId",
            table: "Promotions",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CyclesScolaires",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                SchoolId = table.Column<Guid>(type: "TEXT", nullable: false),
                Kind = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                AuthorizationNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CyclesScolaires", x => x.Id);
                table.ForeignKey(
                    name: "FK_CyclesScolaires_Etablissement_SchoolId",
                    column: x => x.SchoolId,
                    principalTable: "Etablissement",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CyclesScolaires_SchoolId_Kind",
            table: "CyclesScolaires",
            columns: new[] { "SchoolId", "Kind" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Promotions_SchoolCycleId",
            table: "Promotions",
            column: "SchoolCycleId");

        migrationBuilder.CreateTable(
            name: "TarifsCyclesScolaires",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                SchoolCycleId = table.Column<Guid>(type: "TEXT", nullable: false),
                AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                ChargeMonthlyTuition = table.Column<bool>(type: "INTEGER", nullable: false),
                MonthlyAmount = table.Column<long>(type: "INTEGER", nullable: false),
                LegalBasis = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TarifsCyclesScolaires", x => x.Id);
                table.ForeignKey(
                    name: "FK_TarifsCyclesScolaires_CyclesScolaires_SchoolCycleId",
                    column: x => x.SchoolCycleId,
                    principalTable: "CyclesScolaires",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TarifsCyclesScolaires_AnneesScolaires_AcademicYearId",
                    column: x => x.AcademicYearId,
                    principalTable: "AnneesScolaires",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TarifsCyclesScolaires_SchoolCycleId_AcademicYearId",
            table: "TarifsCyclesScolaires",
            columns: new[] { "SchoolCycleId", "AcademicYearId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_TarifsCyclesScolaires_AcademicYearId",
            table: "TarifsCyclesScolaires",
            column: "AcademicYearId");

        migrationBuilder.CreateTable(
            name: "PeriodesCyclesScolaires",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                SchoolCycleId = table.Column<Guid>(type: "TEXT", nullable: false),
                AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                TermCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                Order = table.Column<int>(type: "INTEGER", nullable: false),
                Mode = table.Column<int>(type: "INTEGER", nullable: false),
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
                table.PrimaryKey("PK_PeriodesCyclesScolaires", x => x.Id);
                table.ForeignKey(
                    name: "FK_PeriodesCyclesScolaires_CyclesScolaires_SchoolCycleId",
                    column: x => x.SchoolCycleId,
                    principalTable: "CyclesScolaires",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PeriodesCyclesScolaires_AnneesScolaires_AcademicYearId",
                    column: x => x.AcademicYearId,
                    principalTable: "AnneesScolaires",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PeriodesCyclesScolaires_SchoolCycleId_AcademicYearId_Code",
            table: "PeriodesCyclesScolaires",
            columns: new[] { "SchoolCycleId", "AcademicYearId", "Code" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_PeriodesCyclesScolaires_SchoolCycleId_AcademicYearId_Order",
            table: "PeriodesCyclesScolaires",
            columns: new[] { "SchoolCycleId", "AcademicYearId", "Order" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_PeriodesCyclesScolaires_AcademicYearId",
            table: "PeriodesCyclesScolaires",
            column: "AcademicYearId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PeriodesCyclesScolaires");
        migrationBuilder.DropTable(name: "TarifsCyclesScolaires");

        migrationBuilder.DropIndex(
            name: "IX_Promotions_SchoolCycleId",
            table: "Promotions");
        migrationBuilder.DropColumn(name: "SchoolCycleId", table: "Promotions");
        migrationBuilder.DropTable(name: "CyclesScolaires");
        migrationBuilder.DropColumn(name: "Governance", table: "Etablissement");
    }
}
