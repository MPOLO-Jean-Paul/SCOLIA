using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>
/// Portée d'un règlement pédagogique. Un complexe scolaire possède un règlement
/// par cycle ; une université possède un seul règlement LMD par année.
/// Valeurs persistées : ne jamais renuméroter. ITM/IM n'a pas de règlement ici :
/// son moteur de délibération historique reste inchangé.
/// </summary>
public enum RegulationScope
{
    Maternelle = 1,
    Primaire = 2,
    Secondaire = 3,
    SuperieurLmd = 10,
}

/// <summary>Un règlement proposé reste modifiable ; un règlement validé est figé.</summary>
public enum RegulationStatus
{
    Brouillon = 0,
    Valide = 1,
}

/// <summary>
/// Règles d'évaluation et de délibération d'une année scolaire pour un cycle EPST
/// ou pour l'université. SCOLIA propose des valeurs RDC usuelles ; l'établissement
/// les relit, les ajuste puis les valide. Aucun bulletin officiel, aucune décision
/// de jury n'est produite tant que le règlement de la portée n'est pas validé.
/// </summary>
public sealed class AcademicRegulation : Entity
{
    public Guid AcademicYearId { get; set; }
    public RegulationScope Scope { get; set; }
    public RegulationStatus Status { get; set; } = RegulationStatus.Brouillon;

    // ---- EPST (primaire, secondaire, technique) ----
    /// <summary>Nombre de trimestres (primaire : 3) ou de semestres (secondaire : 2).</summary>
    public int TermsCount { get; set; }
    /// <summary>Périodes de travail journalier par trimestre ou semestre (habituellement 2).</summary>
    public int PeriodsPerTerm { get; set; }
    /// <summary>Le maximum de l'examen vaut ce multiple du maximum d'une période (habituellement 2).</summary>
    public decimal ExamMaxFactor { get; set; }
    /// <summary>Pourcentage global minimal pour réussir l'année.</summary>
    public decimal PassPercent { get; set; }
    /// <summary>Pourcentage minimal dans une branche pour ne pas y être en échec.</summary>
    public decimal SubjectPassPercent { get; set; }
    /// <summary>Nombre maximal de branches en échec admises au repêchage (seconde session).</summary>
    public int MaxRepechageSubjects { get; set; }
    /// <summary>Afficher la place de l'élève sur l'effectif de la classe.</summary>
    public bool ShowRank { get; set; }

    // ---- Université LMD ----
    /// <summary>Note maximale d'un élément constitutif (habituellement 20).</summary>
    public decimal MarkScale { get; set; }
    /// <summary>Moyenne minimale d'une UE pour l'acquérir et capitaliser ses crédits.</summary>
    public decimal UeValidationMark { get; set; }
    /// <summary>Compensation entre les EC d'une même UE (moyenne pondérée de l'UE).</summary>
    public bool CompensateWithinUe { get; set; }
    /// <summary>Compensation entre les UE d'un même semestre.</summary>
    public bool CompensateBetweenUe { get; set; }
    /// <summary>Crédits non acquis tolérés pour passer à l'année supérieure avec dettes.</summary>
    public decimal MaxDebtCredits { get; set; }

    /// <summary>Texte de référence de l'établissement (instruction, décision du conseil...).</summary>
    public string? LegalReference { get; set; }
    public string? ValidatedByName { get; set; }
    public DateTime? ValidatedAtUtc { get; set; }

    public bool IsValidated => Status == RegulationStatus.Valide;
}

public static class AcademicRegulationRules
{
    /// <summary>Crédits d'une année LMD complète : deux semestres de 30 crédits (décret 22/39).</summary>
    public const decimal LmdCreditsPerYear = 2 * LmdCreditPlanRules.CreditsPerSemester;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException("reglement", message);
    }

    public static bool IsEpst(RegulationScope scope) =>
        scope is RegulationScope.Primaire or RegulationScope.Secondaire;

    public static string Label(RegulationScope scope) => scope switch
    {
        RegulationScope.Maternelle => "Maternelle",
        RegulationScope.Primaire => "Primaire",
        RegulationScope.Secondaire => "Secondaire et technique",
        RegulationScope.SuperieurLmd => "Université (LMD)",
        _ => scope.ToString(),
    };

    public static RegulationScope ScopeOf(SchoolCycleKind kind) => kind switch
    {
        SchoolCycleKind.Maternelle => RegulationScope.Maternelle,
        SchoolCycleKind.Primaire => RegulationScope.Primaire,
        SchoolCycleKind.Secondaire => RegulationScope.Secondaire,
        _ => throw new DomainException("reglement", "Cycle scolaire inconnu."),
    };

    /// <summary>
    /// Portées attendues pour l'établissement. ITM/IM n'en a aucune : ses règles
    /// restent celles du moteur de délibération historique.
    /// </summary>
    public static IReadOnlyList<RegulationScope> ScopesFor(string? institutionType,
        IEnumerable<SchoolCycleKind> activeCycles)
    {
        var profile = InstitutionProfiles.Canonicalize(institutionType);
        return profile switch
        {
            InstitutionProfiles.SuperieurLmd => [RegulationScope.SuperieurLmd],
            InstitutionProfiles.Maternelle => [RegulationScope.Maternelle],
            InstitutionProfiles.Primaire => [RegulationScope.Primaire],
            InstitutionProfiles.Secondaire => [RegulationScope.Secondaire],
            InstitutionProfiles.ComplexeScolaire =>
                activeCycles.Distinct().OrderBy(k => k).Select(ScopeOf).ToList(),
            _ => [],
        };
    }

    /// <summary>
    /// Proposition de départ conforme aux usages RDC, toujours à relire par
    /// l'établissement : elle n'a aucune valeur officielle avant validation.
    /// </summary>
    public static AcademicRegulation DefaultFor(RegulationScope scope, Guid academicYearId)
    {
        var r = new AcademicRegulation { AcademicYearId = academicYearId, Scope = scope };
        switch (scope)
        {
            case RegulationScope.Primaire:
                r.TermsCount = 3; r.PeriodsPerTerm = 2; r.ExamMaxFactor = 2;
                r.PassPercent = 50; r.SubjectPassPercent = 50; r.MaxRepechageSubjects = 2; r.ShowRank = true;
                break;
            case RegulationScope.Secondaire:
                r.TermsCount = 2; r.PeriodsPerTerm = 2; r.ExamMaxFactor = 2;
                r.PassPercent = 50; r.SubjectPassPercent = 50; r.MaxRepechageSubjects = 2; r.ShowRank = true;
                break;
            case RegulationScope.SuperieurLmd:
                r.MarkScale = 20; r.UeValidationMark = 10;
                r.CompensateWithinUe = true; r.CompensateBetweenUe = false; r.MaxDebtCredits = 15;
                break;
            case RegulationScope.Maternelle:
                // Observation du développement : pas de note, pas de seuil, pas de classement.
                r.TermsCount = 3;
                break;
            default:
                throw new DomainException("reglement", "Portée de règlement inconnue.");
        }
        return r;
    }

    public static void Validate(AcademicRegulation r)
    {
        Require(Enum.IsDefined(r.Scope), "Portée de règlement inconnue.");
        Require(r.AcademicYearId != Guid.Empty, "Le règlement doit appartenir à une année scolaire.");
        Require(r.LegalReference is null || r.LegalReference.Length <= 300,
            "La référence du règlement doit comporter au maximum 300 caractères.");
        if (IsEpst(r.Scope))
        {
            Require(r.TermsCount is 2 or 3,
                "L'année compte 2 semestres ou 3 trimestres.");
            Require(r.PeriodsPerTerm is >= 1 and <= 3,
                "Chaque trimestre ou semestre compte de 1 à 3 périodes.");
            Require(r.ExamMaxFactor is >= 1 and <= 4,
                "Le maximum de l'examen vaut de 1 à 4 fois celui d'une période.");
            Require(r.PassPercent is > 0 and <= 100, "Le seuil de réussite est compris entre 1 et 100 %.");
            Require(r.SubjectPassPercent is > 0 and <= 100,
                "Le seuil d'une branche est compris entre 1 et 100 %.");
            Require(r.MaxRepechageSubjects is >= 0 and <= 5,
                "Le repêchage concerne de 0 à 5 branches.");
            return;
        }
        if (r.Scope == RegulationScope.SuperieurLmd)
        {
            Require(r.MarkScale is >= 10 and <= 100, "L'échelle des notes est comprise entre 10 et 100.");
            Require(r.UeValidationMark > 0 && r.UeValidationMark <= r.MarkScale,
                "La note de validation d'une UE doit être positive et ne pas dépasser l'échelle.");
            Require(r.MaxDebtCredits >= 0 && r.MaxDebtCredits < LmdCreditsPerYear,
                "Les crédits en dette autorisés doivent être inférieurs aux 60 crédits de l'année.");
            Require(!r.CompensateBetweenUe || r.CompensateWithinUe,
                "La compensation entre UE suppose la compensation entre les EC d'une même UE.");
            return;
        }
        Require(r.TermsCount is >= 1 and <= 3, "La maternelle compte de 1 à 3 trimestres d'observation.");
    }

    public static void EnsureEditable(AcademicRegulation r) =>
        Require(!r.IsValidated,
            "Ce règlement est validé : rouvrez-le avant toute modification.");
}
