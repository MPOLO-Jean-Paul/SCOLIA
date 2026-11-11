using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;

namespace Scolia.Domain.School;

/// <summary>
/// Hiérarchie du supérieur uniquement : faculté, département puis filière.
/// Les sections et promotions ITM déjà existantes ne sont jamais converties.
/// </summary>
public enum AcademicUnitKind
{
    Faculty = 1,
    Department = 2,
    Filiere = 3,
}

public sealed class AcademicStructureUnit : Entity
{
    public Guid? ParentId { get; set; }
    public AcademicUnitKind Kind { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Référentiel distinct des cours, évaluations et notes ITM existants.
/// Pour le primaire et le secondaire, le cours reste annuel.
/// Pour le LMD, une UE est composée d'EC rattachés au même semestre.
/// </summary>
public enum CurriculumItemKind
{
    AnnualSubject = 1,
    TeachingUnit = 2,
    TeachingElement = 3,
}

public sealed class CurriculumItem : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid PromotionId { get; set; }
    /// <summary>Filière LMD, absent pour le primaire et le secondaire.</summary>
    public Guid? AcademicUnitId { get; set; }
    /// <summary>UE parente pour un EC ; absent pour les UE et les cours annuels.</summary>
    public Guid? ParentItemId { get; set; }
    public CurriculumItemKind Kind { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CourseSemester Semester { get; set; } = CourseSemester.Annuel;
    /// <summary>Crédits du LMD ; toujours zéro pour les matières annuelles.</summary>
    public decimal Credits { get; set; }
    /// <summary>Coefficient de la branche annuelle ou de l'EC, selon les règles locales.</summary>
    public decimal Coefficient { get; set; } = 1;
    public decimal Hours { get; set; }
    public bool IsActive { get; set; } = true;
}

public static class CurriculumStructureRules
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException("programmePedagogique", message);
    }

    private static void RequireCode(string? code, string what)
    {
        Require(!string.IsNullOrWhiteSpace(code) && code.Length <= 24 &&
                code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            $"Code {what} invalide (1 à 24 caractères : lettres, chiffres, - ou _).");
    }

    /// <summary>Vérifie la hiérarchie des facultés, départements et filières du LMD.</summary>
    public static void ValidateHierarchy(string institutionType, IReadOnlyCollection<AcademicStructureUnit> nodes)
    {
        Require(InstitutionProfiles.IsKnown(institutionType), "Type d'établissement inconnu.");
        if (institutionType != InstitutionProfiles.SuperieurLmd)
        {
            Require(nodes.Count == 0, "Une hiérarchie faculté/département/filière ne concerne que le supérieur LMD.");
            return;
        }

        var byId = nodes.ToDictionary(n => n.Id);
        var siblingCodes = new HashSet<(Guid? ParentId, AcademicUnitKind Kind, string Code)>();
        foreach (var node in nodes)
        {
            Require(Enum.IsDefined(node.Kind), "Nature d'unité académique inconnue.");
            RequireCode(node.Code, "de structure");
            Require(!string.IsNullOrWhiteSpace(node.Name) && node.Name.Length <= 160,
                "Le nom de l'unité académique doit comporter au maximum 160 caractères.");
            Require(siblingCodes.Add((node.ParentId, node.Kind, node.Code.ToUpperInvariant())),
                "Le code d'une unité académique doit être unique dans le même niveau et sous le même parent.");
            if (node.Kind == AcademicUnitKind.Faculty)
            {
                Require(node.ParentId is null, "Une faculté est à la racine de l'établissement.");
                continue;
            }
            Require(node.ParentId is { } parentId && byId.TryGetValue(parentId, out _),
                "Département ou filière sans parent défini.");
            var parent = byId[node.ParentId!.Value];
            Require(node.Kind == AcademicUnitKind.Department && parent.Kind == AcademicUnitKind.Faculty ||
                    node.Kind == AcademicUnitKind.Filiere && parent.Kind == AcademicUnitKind.Department,
                "La hiérarchie LMD suit faculté → département → filière.");
        }
    }

    /// <summary>
    /// Validation d'un programme complet, avant activation. Cette fonction
    /// n'enregistre pas de cours, ne modifie pas les résultats ITM et ne
    /// suppose pas que les dates/coefficients sont identiques entre écoles.
    /// </summary>
    public static void ValidateCurriculum(string institutionType, AcademicYear year,
        IReadOnlyCollection<CurriculumItem> items, IReadOnlyCollection<AcademicStructureUnit> nodes)
    {
        Require(InstitutionProfiles.IsKnown(institutionType), "Type d'établissement inconnu.");
        ValidateHierarchy(institutionType, nodes);
        if (institutionType == InstitutionProfiles.ItmIm || institutionType == InstitutionProfiles.ItmIem)
        {
            Require(items.Count == 0, "Les cursus ITM/IM continuent d'utiliser leur moteur existant.");
            return;
        }

        Require(items.Count > 0, "Le programme doit contenir au moins une matière.");
        var byId = items.ToDictionary(i => i.Id);
        var pathIds = nodes.Where(n => n.Kind == AcademicUnitKind.Filiere && n.IsActive).Select(n => n.Id).ToHashSet();
        var codes = new HashSet<(Guid PromotionId, Guid? AcademicUnitId, CourseSemester Semester, string Code)>();

        foreach (var item in items)
        {
            Require(item.AcademicYearId == year.Id && item.PromotionId != Guid.Empty,
                "Chaque matière doit appartenir à une promotion de l'année courante.");
            RequireCode(item.Code, "du cours");
            Require(!string.IsNullOrWhiteSpace(item.Name) && item.Name.Length <= 160,
                "Le titre du cours doit comporter au maximum 160 caractères.");
            Require(item.Hours >= 0 && item.Coefficient > 0,
                "Le volume horaire doit être positif ou nul, et le coefficient strictement positif.");
            Require(codes.Add((item.PromotionId, item.AcademicUnitId, item.Semester, item.Code.ToUpperInvariant())),
                "Deux matières du même semestre et de la même promotion ne peuvent partager un code.");
            if (institutionType is InstitutionProfiles.Primaire or InstitutionProfiles.Secondaire)
            {
                Require(item.Kind == CurriculumItemKind.AnnualSubject &&
                        item.Semester == CourseSemester.Annuel && item.ParentItemId is null &&
                        item.AcademicUnitId is null && item.Credits == 0,
                    "Le primaire et le secondaire utilisent exclusivement des matières annuelles, sans crédits LMD.");
                continue;
            }

            Require(item.Kind is CurriculumItemKind.TeachingUnit or CurriculumItemKind.TeachingElement,
                "Le LMD utilise uniquement des unités d'enseignement (UE) et leurs éléments constitutifs (EC).");
            Require(item.Semester is CourseSemester.S1 or CourseSemester.S2,
                "Chaque UE et chaque EC doivent appartenir exclusivement à S1 ou S2.");
            Require(item.AcademicUnitId is { } unitId && pathIds.Contains(unitId),
                "Les UE et EC doivent être rattachés à une filière LMD active.");
            Require(item.Credits > 0, "Chaque UE et EC doit avoir un nombre de crédits strictement positif.");
            if (item.Kind == CurriculumItemKind.TeachingUnit)
            {
                Require(item.ParentItemId is null, "Une UE ne peut pas dépendre d'une autre UE.");
            }
            else
            {
                Require(item.ParentItemId is { } parentId && byId.TryGetValue(parentId, out _),
                    "Chaque EC doit appartenir à une UE existante.");
                var ue = byId[item.ParentItemId!.Value];
                Require(ue.Kind == CurriculumItemKind.TeachingUnit &&
                        ue.PromotionId == item.PromotionId &&
                        ue.AcademicUnitId == item.AcademicUnitId &&
                        ue.Semester == item.Semester && ue.AcademicYearId == item.AcademicYearId,
                    "L'EC doit appartenir à une UE de la même filière, promotion, année et semestre.");
            }
        }

        if (institutionType == InstitutionProfiles.SuperieurLmd)
        {
            foreach (var ue in items.Where(i => i.Kind == CurriculumItemKind.TeachingUnit))
            {
                var elements = items.Where(i => i.Kind == CurriculumItemKind.TeachingElement &&
                                               i.ParentItemId == ue.Id).ToArray();
                Require(elements.Length > 0, "Une UE ne peut pas être validée sans élément constitutif.");
                Require(elements.Sum(i => i.Credits) == ue.Credits,
                    "La somme des crédits des EC doit correspondre aux crédits de leur UE.");
            }
        }
    }
}
