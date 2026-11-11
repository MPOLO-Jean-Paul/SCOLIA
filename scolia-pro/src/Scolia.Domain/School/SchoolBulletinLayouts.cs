using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>
/// Mise en page pédagogique provisoire, indépendante de la certification
/// ministérielle. Les matières, domaines, maxima et seuils sont configurés
/// ailleurs et ne doivent jamais être inventés ici.
/// </summary>
public sealed record SchoolBulletinLayout(
    string Code,
    string PreviewTitle,
    string EntryHeading,
    string PeriodHeading,
    bool UsesNumericScores,
    bool IsOfficial,
    string StatusNote);

public static class SchoolBulletinLayouts
{
    public static SchoolBulletinLayout For(SchoolCycleKind kind) => kind switch
    {
        SchoolCycleKind.Maternelle => new(
            "MATERNELLE_OBSERVATIONS",
            "Carnet d'observations — maternelle",
            "Activités éducatives",
            "Période d'observation",
            UsesNumericScores: false,
            IsOfficial: false,
            StatusNote: "Carnet pédagogique descriptif. Un éventuel barème officiel de maternelle devra être validé et configuré séparément."),
        SchoolCycleKind.Primaire => new(
            "PRIMAIRE_TRIMESTRES",
            "Relevé des évaluations — primaire",
            "Branches et domaines",
            "Trimestre",
            UsesNumericScores: true,
            IsOfficial: false,
            StatusNote: "Aperçu pédagogique par trimestre. Les domaines, maxima, totaux et décisions restent à valider pour la classe et l'année scolaire."),
        SchoolCycleKind.Secondaire => new(
            "SECONDAIRE_SEMESTRES",
            "Relevé des évaluations — secondaire",
            "Branches et options",
            "Semestre",
            UsesNumericScores: true,
            IsOfficial: false,
            StatusNote: "Aperçu pédagogique par semestre. Les options techniques, examens et règles de délibération restent à valider."),
        _ => throw new DomainException("bulletinCycle",
            "Le modèle de bulletin doit correspondre à un cycle maternel, primaire ou secondaire."),
    };
}
