using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations;

/// <summary>
/// Migration strictement additive : observations de maternelle et cotes des
/// cycles scolaires distinctes des tables Notes/Deliberation ITM existantes.
/// </summary>
[DbContext(typeof(ScoliaDbContext))]
[Migration("20261111161000_CycleSchoolEvaluations")]
public sealed class CycleSchoolEvaluations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EvaluationsCyclesScolaires",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                AcademicYearId = table.Column<Guid>(type: "TEXT", nullable: false),
                SchoolCycleId = table.Column<Guid>(type: "TEXT", nullable: false),
                PromotionId = table.Column<Guid>(type: "TEXT", nullable: false),
                EnrollmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                SchoolCyclePeriodId = table.Column<Guid>(type: "TEXT", nullable: false),
                CurriculumItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                EvaluationCode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                Mode = table.Column<int>(type: "INTEGER", nullable: false),
                Observation = table.Column<string>(type: "TEXT", maxLength: 800, nullable: true),
                Score = table.Column<long>(type: "INTEGER", nullable: true),
                MaxScore = table.Column<long>(type: "INTEGER", nullable: true),
                Comment = table.Column<string>(type: "TEXT", maxLength: 800, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                Version = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationsCyclesScolaires", x => x.Id);
                table.ForeignKey("FK_EvaluationsCyclesScolaires_AnneesScolaires_AcademicYearId",
                    x => x.AcademicYearId, "AnneesScolaires", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EvaluationsCyclesScolaires_CyclesScolaires_SchoolCycleId",
                    x => x.SchoolCycleId, "CyclesScolaires", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EvaluationsCyclesScolaires_Promotions_PromotionId",
                    x => x.PromotionId, "Promotions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EvaluationsCyclesScolaires_Inscriptions_EnrollmentId",
                    x => x.EnrollmentId, "Inscriptions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EvaluationsCyclesScolaires_PeriodesCyclesScolaires_SchoolCyclePeriodId",
                    x => x.SchoolCyclePeriodId, "PeriodesCyclesScolaires", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_EvaluationsCyclesScolaires_ProgrammesPedagogiques_CurriculumItemId",
                    x => x.CurriculumItemId, "ProgrammesPedagogiques", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_EvaluationsCyclesScolaires_AcademicYearId",
            table: "EvaluationsCyclesScolaires", column: "AcademicYearId");
        migrationBuilder.CreateIndex(
            name: "IX_EvaluationsCyclesScolaires_SchoolCycleId_AcademicYearId_PromotionId",
            table: "EvaluationsCyclesScolaires",
            columns: new[] { "SchoolCycleId", "AcademicYearId", "PromotionId" });
        migrationBuilder.CreateIndex(
            name: "IX_EvaluationsCyclesScolaires_PromotionId",
            table: "EvaluationsCyclesScolaires", column: "PromotionId");
        migrationBuilder.CreateIndex(
            name: "IX_EvaluationsCyclesScolaires_SchoolCyclePeriodId",
            table: "EvaluationsCyclesScolaires", column: "SchoolCyclePeriodId");
        migrationBuilder.CreateIndex(
            name: "IX_EvaluationsCyclesScolaires_CurriculumItemId",
            table: "EvaluationsCyclesScolaires", column: "CurriculumItemId");
        migrationBuilder.CreateIndex(
            name: "IX_EvaluationsCyclesScolaires_EnrollmentId_SchoolCyclePeriodId_EvaluationCode",
            table: "EvaluationsCyclesScolaires",
            columns: new[] { "EnrollmentId", "SchoolCyclePeriodId", "EvaluationCode" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "EvaluationsCyclesScolaires");
}
