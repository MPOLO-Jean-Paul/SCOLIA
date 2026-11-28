using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>
/// Mise en page pédagogique provisoire, indépendante de la certification ministérielle.
/// Aucune règle officielle de maxima, de seuil, de rang ou de passage n'est déduite
/// d'une classe, d'un degré ou d'une option.
/// </summary>
public sealed record SchoolBulletinLayout(
    string Code,
    string PreviewTitle,
    string EntryHeading,
    string PeriodHeading,
    bool UsesNumericScores,
    bool IsOfficial,
    string StatusNote,
    string GradeBand = "Cycle non précisé");

public static class SchoolBulletinLayouts
{
    /// <summary>Modèle générique conservé pour les appelants existants.</summary>
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

    /// <summary>
    /// Détermine une variante pédagogique pour la classe, sans prétendre reproduire
    /// une maquette ministérielle. Un niveau inconnu reste sur le modèle générique
    /// au lieu de deviner à partir du libellé de la classe.
    /// Les options sont rattachées aux classes dans le domaine scolaire.
    /// </summary>
    public static SchoolBulletinLayout For(SchoolCycleKind kind, int level)
    {
        var template = For(kind);
        return (kind, level) switch
        {
            (SchoolCycleKind.Maternelle, >= 1 and <= 3) => template with
            {
                Code = $"MATERNELLE_{level}_OBSERVATIONS",
                PreviewTitle = $"Carnet d'observations — {level}{(level == 1 ? "re" : "e")} maternelle",
                GradeBand = "Préscolaire",
            },
            (SchoolCycleKind.Primaire, >= 1 and <= 2) => template with
            {
                Code = "PRIMAIRE_DEGRE_ELEMENTAIRE",
                PreviewTitle = "Relevé des évaluations — degré élémentaire",
                GradeBand = "1re et 2e primaire",
            },
            (SchoolCycleKind.Primaire, >= 3 and <= 4) => template with
            {
                Code = "PRIMAIRE_DEGRE_MOYEN",
                PreviewTitle = "Relevé des évaluations — degré moyen",
                GradeBand = "3e et 4e primaire",
            },
            (SchoolCycleKind.Primaire, >= 5 and <= 6) => template with
            {
                Code = "PRIMAIRE_DEGRE_TERMINAL",
                PreviewTitle = "Relevé des évaluations — degré terminal",
                GradeBand = "5e et 6e primaire",
            },
            (SchoolCycleKind.Secondaire, >= 1 and <= 2) => template with
            {
                Code = "SECONDAIRE_CTEB",
                PreviewTitle = "Relevé des évaluations — cycle terminal de l'éducation de base",
                GradeBand = "7e et 8e de l'éducation de base",
            },
            (SchoolCycleKind.Secondaire, >= 3 and <= 6) => template with
            {
                Code = "SECONDAIRE_HUMANITES",
                PreviewTitle = "Relevé des évaluations — humanités",
                GradeBand = "Humanités générales ou techniques",
            },
            _ => template,
        };
    }
}
