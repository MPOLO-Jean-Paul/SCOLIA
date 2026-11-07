using Scolia.Domain.Deliberation;

namespace Scolia.Application.Common;

/// <summary>Libellés échangés avec les clients pour la délibération : « S1 », « S2 », « Annuel », « Appui », « Admis »…</summary>
public static class DeliberationText
{
    private static readonly (CourseSemester, string)[] SemesterLabels =
        [(CourseSemester.S1, "Semestre 1"), (CourseSemester.S2, "Semestre 2"), (CourseSemester.Annuel, "Annual"), (CourseSemester.Annuel, "Annuelle")];
    private static readonly (DeliberationPeriod, string)[] PeriodLabels =
        [(DeliberationPeriod.S1, "Semestre 1"), (DeliberationPeriod.S2, "Semestre 2"), (DeliberationPeriod.Annuel, "Jury"), (DeliberationPeriod.Annuel, "Final"), (DeliberationPeriod.Annuel, "Jury annuel")];
    private static readonly (MarkKind, string)[] KindLabels =
        [(MarkKind.CoteS1, "S1"), (MarkKind.CoteS2, "S2"), (MarkKind.Appui, "Moyenne d'appui"), (MarkKind.Appui, "Moyenne")];
    private static readonly (Domain.Deliberation.Outcome, string)[] OutcomeLabels = [(Domain.Deliberation.Outcome.Ajourne, "Échec"), (Domain.Deliberation.Outcome.Ajourne, "Ajourné")];

    public static CourseSemester Semester(string? value) => EnumText.Parse(value, "semestre", SemesterLabels);
    public static string Semester(CourseSemester value) => value.ToString();
    public static DeliberationPeriod Period(string? value) => EnumText.Parse(value, "periode", PeriodLabels);
    public static string Period(DeliberationPeriod value) => value.ToString();
    public static MarkKind Kind(string? value) => EnumText.Parse(value, "nature", KindLabels);
    public static string Kind(MarkKind value) => value switch { MarkKind.CoteS1 => "S1", MarkKind.CoteS2 => "S2", _ => "Appui" };
    public static Domain.Deliberation.Outcome Outcome(string? value) => EnumText.Parse(value, "decision", OutcomeLabels);
}
