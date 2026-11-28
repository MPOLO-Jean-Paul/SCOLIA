using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;

namespace Scolia.Domain.School;

/// <summary>
/// Vérifications d'une maquette LMD COMPLÈTE avant sa soumission à validation.
/// Décret RDC n°22/39 du 06/12/2022, articles 4, 6 et 7 : 30 crédits
/// par semestre, charge de travail de 25 h par crédit, les UE portent
/// les crédits. La validation ministérielle de la maquette reste externe.
/// </summary>
public static class LmdCreditPlanRules
{
    public const decimal CreditsPerSemester = 30m;
    public const decimal HoursPerCredit = 25m;

    public static decimal StudentWorkloadHours(decimal ueCredits)
    {
        if (ueCredits <= 0)
            throw new DomainException("creditsLmd", "Le nombre de crédits de l'UE doit être strictement positif.");
        return ueCredits * HoursPerCredit;
    }

    /// <summary>
    /// Contrôle final d'un programme annuel S1/S2 d'une promotion et filière
    /// ou parcours. Il ne valide NI une session d'examens NI une décision de jury.
    /// Les brouillons incomplets restent autorisés par ValidateCurriculum.
    /// </summary>
    public static void ValidateCompleteAcademicYear(
        AcademicYear year, Guid promotionId, Guid academicUnitId,
        IReadOnlyCollection<CurriculumItem> allItems,
        IReadOnlyCollection<AcademicStructureUnit> units)
    {
        if (promotionId == Guid.Empty || academicUnitId == Guid.Empty)
            throw new DomainException("creditsLmd", "La promotion et la filière ou le parcours LMD sont obligatoires.");

        CurriculumStructureRules.ValidateCurriculum(
            InstitutionProfiles.SuperieurLmd, year, allItems, units);

        var items = allItems.Where(i => i.AcademicYearId == year.Id &&
                                         i.PromotionId == promotionId &&
                                         i.AcademicUnitId == academicUnitId).ToArray();
        if (items.Length == 0)
            throw new DomainException("creditsLmd", "Aucun cours attribué à ce parcours pour cette année.");

        foreach (var semester in new[] { CourseSemester.S1, CourseSemester.S2 })
        {
            // Les EC ne comptent PAS dans le total : ils font partie des UE.
            var credits = items.Where(i => i.Kind == CurriculumItemKind.TeachingUnit &&
                                           i.Semester == semester && i.IsActive)
                               .Sum(i => i.Credits);
            if (credits != CreditsPerSemester)
                throw new DomainException("creditsLmd",
                    $"Le programme complet du {semester} doit comprendre 30 crédits d'UE ; actuellement {credits:0.##}.");
        }
    }
}

/// <summary>
/// Appréciation indicative du barème publié dans l'instruction académique
/// ministérielle n°027 du 22 octobre 2025, distincte d'une délibération.
/// </summary>
public sealed record LmdMarkAppreciation(string Code, string Label);

public static class LmdMarkAppreciation2025
{
    public static LmdMarkAppreciation FromNote(decimal noteOutOf20)
    {
        if (noteOutOf20 < 0 || noteOutOf20 > 20)
            throw new DomainException("noteLmd", "La note LMD doit être comprise entre 0 et 20.");
        return noteOutOf20 switch
        {
            >= 18 => new("A", "Excellent"),
            >= 16 => new("B", "Très bien"),
            >= 14 => new("C", "Bien"),
            >= 12 => new("D", "Assez bien"),
            >= 10 => new("E", "Passable"),
            >= 8 => new("F", "Insuffisant"),
            _ => new("G", "Insatisfaisant"),
        };
    }
}
