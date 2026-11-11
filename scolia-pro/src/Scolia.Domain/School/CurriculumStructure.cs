using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;

namespace Scolia.Domain.School;

/// <summary>
/// Structures organisationnelles (faculté/département/filière) et de maquette
/// LMD (domaine/mention/parcours/spécialité), sans les confondre.
/// Deux racines sont autorisées : Faculté et Domaine.
/// Les sections et promotions ITM déjà existantes ne sont jamais converties.
/// </summary>
public enum AcademicUnitKind
{
    Faculty = 1,
    Department = 2,
    Filiere = 3,
    // Identifiants ajoutés : ne jamais renuméroter les valeurs persistées.
    Domain = 4,
    Mention = 5,
    Pathway = 6,
    Speciality = 7,
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
    /// <summary>Filière, parcours ou spécialité LMD ; absent au primaire et secondaire.</summary>
    public Guid? AcademicUnitId { get; set; }
    /// <summary>UE parente pour un EC ; absent pour les UE et les cours annuels.</summary>
    public Guid? ParentItemId { get; set; }
    public CurriculumItemKind Kind { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CourseSemester Semester { get; set; } = CourseSemester.Annuel;
    /// <summary>Crédits obligatoires pour l'UE ; répartition sur EC facultative.
    /// Zéro sur tous les EC signifie « non réparti » ; matières annuelles : zéro.</summary>
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

    /// <summary>
    /// La hiérarchie administrative et la hiérarchie pédagogique ont des
    /// racines distinctes : un Domaine peut être interfacultaire (décret 22/39).
    /// </summary>
    public static void ValidateHierarchy(string institutionType, IReadOnlyCollection<AcademicStructureUnit> nodes)
    {
        Require(InstitutionProfiles.IsKnown(institutionType), "Type d'établissement inconnu.");
        if (institutionType != InstitutionProfiles.SuperieurLmd)
        {
            Require(nodes.Count == 0, "Les structures LMD ne concernent que les établissements supérieurs.");
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

            if (node.Kind is AcademicUnitKind.Faculty or AcademicUnitKind.Domain)
            {
                Require(node.ParentId is null, "Les facultés et domaines sont à la racine.");
                continue;
            }
            Require(node.ParentId is { } parentId && byId.TryGetValue(parentId, out _),
                "Unité académique sans parent défini.");
            var parent = byId[node.ParentId!.Value];
            var validParent = (node.Kind, parent.Kind) switch
            {
                (AcademicUnitKind.Department, AcademicUnitKind.Faculty) => true,
                (AcademicUnitKind.Filiere, AcademicUnitKind.Department) => true,
                (AcademicUnitKind.Mention, AcademicUnitKind.Domain) => true,
                (AcademicUnitKind.Pathway, AcademicUnitKind.Mention) => true,
                (AcademicUnitKind.Speciality, AcademicUnitKind.Pathway) => true,
                _ => false,
            };
            Require(validParent,
                "Hiérarchie LMD attendue : faculté → département → filière, ou domaine → mention → parcours → spécialité.");
        }
    }

    private static bool HasActiveAncestors(AcademicStructureUnit node,
        IReadOnlyDictionary<Guid, AcademicStructureUnit> byId)
    {
        // ValidateHierarchy a déjà contrôlé les relations et empêche les cycles.
        while (true)
        {
            if (!node.IsActive) return false;
            if (node.ParentId is not { } parentId) return true;
            node = byId[parentId];
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
        var nodeById = nodes.ToDictionary(n => n.Id);
        var pathIds = nodes.Where(n => n.Kind is AcademicUnitKind.Filiere
                                    or AcademicUnitKind.Pathway or AcademicUnitKind.Speciality)
            .Where(n => HasActiveAncestors(n, nodeById)).Select(n => n.Id).ToHashSet();
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
                "Les UE et EC doivent être rattachés à une filière, un parcours ou une spécialité actifs.");
            if (item.Kind == CurriculumItemKind.TeachingUnit)
            {
                Require(item.Credits > 0, "Chaque UE doit avoir un nombre de crédits strictement positif.");
                Require(item.ParentItemId is null, "Une UE ne peut pas dépendre d'une autre UE.");
            }
            else
            {
                // Le décret attribue les crédits à l'UE ; un EC peut porter
                // zéro si sa répartition n'est pas renseignée dans la maquette.
                Require(item.Credits >= 0, "Les crédits d'un EC ne peuvent pas être négatifs.");
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
                if (elements.Any(i => i.Credits > 0))
                {
                    Require(elements.All(i => i.Credits > 0),
                        "La répartition des crédits entre EC doit être complète, ou entièrement absente.");
                    Require(elements.Sum(i => i.Credits) == ue.Credits,
                        "Quand les crédits sont répartis, leur somme doit être égale à ceux de l'UE.");
                }
            }
        }
    }
}
