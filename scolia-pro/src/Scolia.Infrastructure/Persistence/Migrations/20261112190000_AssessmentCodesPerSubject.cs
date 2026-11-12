using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scolia.Infrastructure.Persistence.Migrations;

/// <summary>
/// Les évaluations de plusieurs matières peuvent utiliser le même code dans une
/// période. Modification uniquement des index : aucune ligne, note ou observation
/// existante n'est réécrite et aucun schéma ITM/IM n'est modifié.
/// </summary>
[DbContext(typeof(ScoliaDbContext))]
[Migration("20261112190000_AssessmentCodesPerSubject")]
public sealed class AssessmentCodesPerSubject : Migration
{
    private const string Table = "EvaluationsCyclesScolaires";
    private const string FreeActivityIndex =
        "IX_EvaluationsCyclesScolaires_EnrollmentId_SchoolCyclePeriodId_EvaluationCode";
    private const string SubjectIndex =
        "IX_EvaluationsCyclesScolaires_EnrollmentId_SchoolCyclePeriodId_CurriculumItemId_EvaluationCode";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: FreeActivityIndex, table: Table);
        migrationBuilder.CreateIndex(
            name: FreeActivityIndex, table: Table,
            columns: new[] { "EnrollmentId", "SchoolCyclePeriodId", "EvaluationCode" },
            unique: true, filter: "\"CurriculumItemId\" IS NULL");
        migrationBuilder.CreateIndex(
            name: SubjectIndex, table: Table,
            columns: new[] { "EnrollmentId", "SchoolCyclePeriodId", "CurriculumItemId", "EvaluationCode" },
            unique: true, filter: "\"CurriculumItemId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: SubjectIndex, table: Table);
        migrationBuilder.DropIndex(name: FreeActivityIndex, table: Table);
        // Un retour en arrière peut être refusé si des matières différentes
        // utilisent déjà le même code : aucune donnée ne doit être supprimée.
        migrationBuilder.CreateIndex(
            name: FreeActivityIndex, table: Table,
            columns: new[] { "EnrollmentId", "SchoolCyclePeriodId", "EvaluationCode" },
            unique: true);
    }
}
