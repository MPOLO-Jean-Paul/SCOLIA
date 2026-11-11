using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>Nature juridique de l'établissement, indépendante du cycle.</summary>
public enum SchoolGovernanceKind
{
    Prive = 1,
    Public = 2,
}

/// <summary>Un complexe scolaire peut proposer plusieurs cycles simultanément.</summary>
public enum SchoolCycleKind
{
    Maternelle = 1,
    Primaire = 2,
    Secondaire = 3,
}

/// <summary>
/// Un seul établissement et une seule base pour toutes les sections.
/// Chaque cycle peut disposer de son propre code d'agrément et de ses tarifs.
/// </summary>
public sealed class SchoolCycle : Entity
{
    public Guid SchoolId { get; set; }
    public SchoolCycleKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AuthorizationNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Modèle de tarifs par cycle et par année. Les factures réelles restent
/// des FeeItem liés aux promotions, sans modification de la caisse ITM.
/// </summary>
public sealed class SchoolCycleFeePolicy : Entity
{
    public Guid SchoolCycleId { get; set; }
    public Guid AcademicYearId { get; set; }
    public bool ChargeMonthlyTuition { get; set; }
    public decimal MonthlyAmount { get; set; }
    public string? LegalBasis { get; set; }
}

/// <summary>En maternelle, l'évaluation est une observation, pas un examen noté.</summary>
public enum SchoolCycleAssessmentMode
{
    Observation = 1,
    Evaluation = 2,
}

/// <summary>Calendrier propre à un cycle, sans conflit entre P1 primaire et P1 secondaire.</summary>
public sealed class SchoolCyclePeriod : Entity
{
    public Guid SchoolCycleId { get; set; }
    public Guid AcademicYearId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TermCode { get; set; } = string.Empty;
    public int Order { get; set; }
    public SchoolCycleAssessmentMode Mode { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

/// <summary>
/// Règles préparatoires des complexes privés. Rien n'est facturé à la création
/// des cycles : l'application des tarifs aux classes viendra avec les écrans
/// et le workflow de validation de l'établissement.
/// </summary>
public static class SchoolCycleRules
{
    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new DomainException("cycleScolaire", detail);
    }

    public static void ValidateOffering(SchoolProfile school, IReadOnlyCollection<SchoolCycle> offered)
    {
        var profile = InstitutionProfiles.Canonicalize(school.InstitutionType);
        Require(InstitutionProfiles.IsKnown(profile), "Profil d'établissement inconnu.");
        Require(Enum.IsDefined(school.Governance), "Régime de gestion de l'établissement inconnu.");
        var types = new HashSet<SchoolCycleKind>();
        foreach (var cycle in offered)
        {
            Require(cycle.SchoolId == school.Id, "Le cycle appartient à un autre établissement.");
            Require(Enum.IsDefined(cycle.Kind), "Cycle maternel/primaire/secondaire inconnu.");
            Require(types.Add(cycle.Kind), "Un cycle ne doit apparaître qu'une seule fois dans l'établissement.");
            Require(!string.IsNullOrWhiteSpace(cycle.Name) && cycle.Name.Length <= 100,
                "Le nom du cycle doit comporter entre 1 et 100 caractères.");
            Require(cycle.AuthorizationNumber is null || cycle.AuthorizationNumber.Length <= 100,
                "La référence de l'agrément du cycle est trop longue.");
        }
        var expected = profile switch
        {
            InstitutionProfiles.Maternelle => SchoolCycleKind.Maternelle,
            InstitutionProfiles.Primaire => SchoolCycleKind.Primaire,
            InstitutionProfiles.Secondaire => SchoolCycleKind.Secondaire,
            _ => (SchoolCycleKind?)null,
        };
        if (expected is { } single)
            Require(offered.Count == 1 && types.Contains(single),
                "Un établissement à cycle unique doit déclarer uniquement le cycle correspondant.");
        else if (profile == InstitutionProfiles.ComplexeScolaire)
            Require(offered.Count is >= 2 and <= 3,
                "Un complexe scolaire doit déclarer deux ou trois cycles parmi maternelle, primaire et secondaire.");
        else
            Require(offered.Count == 0,
                "ITM/IM et supérieur LMD conservent leur configuration historique, sans cycles scolaires annexes.");
    }

    public static void ValidatePromotionCycle(SchoolProfile school, SchoolCycle? cycle, Promotion promotion)
    {
        var profile = InstitutionProfiles.Canonicalize(school.InstitutionType);
        if (profile is InstitutionProfiles.ItmIm or InstitutionProfiles.SuperieurLmd)
        {
            Require(cycle is null && promotion.SchoolCycleId is null,
                "Les promotions ITM/IM et LMD conservent leur modèle existant.");
            return;
        }

        if (cycle is null)
            throw new DomainException("cycleScolaire", "Choisissez le cycle de cette classe.");
        Require(cycle.SchoolId == school.Id && cycle.IsActive && promotion.SchoolCycleId == cycle.Id,
            "Chaque classe maternelle, primaire ou secondaire doit appartenir à un cycle actif.");
        Require(profile == InstitutionProfiles.ComplexeScolaire ||
                profile == cycle.Kind.ToString(),
            "La classe ne correspond pas au profil de l'établissement.");
    }

    public static void ValidateFeePolicy(
        SchoolProfile school, SchoolCycle cycle, AcademicYear year, SchoolCycleFeePolicy plan)
    {
        Require(cycle.SchoolId == school.Id && plan.SchoolCycleId == cycle.Id,
            "La grille tarifaire doit appartenir au cycle de l'établissement.");
        Require(plan.AcademicYearId == year.Id, "La grille tarifaire appartient à une autre année.");
        Require(plan.MonthlyAmount >= 0, "Le montant mensuel doit être positif ou nul.");
        Require(plan.LegalBasis is null || plan.LegalBasis.Length <= 500,
            "Référence de la décision tarifaire trop longue.");

        // Article 77 : la gratuité publique n'est PAS imposée au privé.
        // Le cadre public (dont l'éducation de base) requiert la validation
        // des conditions légales avant toute mise en recouvrement.
        if (school.Governance == SchoolGovernanceKind.Public)
            Require(!plan.ChargeMonthlyTuition && plan.MonthlyAmount == 0,
                "Les frais d'un établissement public ne peuvent pas être activés par la grille privée : validation réglementaire distincte requise.");
    }

    public static void ValidateCalendar(
        SchoolCycle cycle, AcademicYear year, IReadOnlyCollection<SchoolCyclePeriod> periods)
    {
        Require(periods.Count is > 0 and <= 12, "Définissez entre une et douze périodes pédagogiques par cycle.");
        var ordered = periods.OrderBy(p => p.Order).ThenBy(p => p.StartDate).ToArray();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Length; i++)
        {
            var p = ordered[i];
            Require(p.SchoolCycleId == cycle.Id && p.AcademicYearId == year.Id,
                "La période ne correspond pas à l'année et au cycle.");
            Require(p.Order == i + 1, "Les périodes doivent être numérotées à partir de 1.");
            Require(p.Code.Length is > 0 and <= 16 && p.Code.All(char.IsAsciiLetterOrDigit) &&
                    codes.Add(p.Code), "Code de période invalide ou en double dans le cycle.");
            Require(!string.IsNullOrWhiteSpace(p.Name) && p.Name.Length <= 80,
                "Nom de période invalide.");
            Require(!string.IsNullOrWhiteSpace(p.TermCode) && p.TermCode.Length <= 16,
                "Le trimestre ou semestre doit être identifié.");
            Require(p.StartDate >= year.StartDate && p.EndDate <= year.EndDate &&
                    p.StartDate <= p.EndDate, "Dates de période hors de l'année scolaire.");
            Require(i == 0 || ordered[i - 1].EndDate < p.StartDate,
                "Deux périodes du même cycle ne peuvent pas se chevaucher.");
            var required = cycle.Kind == SchoolCycleKind.Maternelle
                ? SchoolCycleAssessmentMode.Observation
                : SchoolCycleAssessmentMode.Evaluation;
            Require(p.Mode == required,
                "La maternelle utilise l'observation ; le primaire et le secondaire utilisent des périodes d'évaluation.");
        }
        // La répartition précise et les dates restent celles du calendrier
        // ministériel de l'année configurée. Ne pas inventer des seuils de notes.
    }
}
