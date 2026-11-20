using Scolia.Domain.Common;
using Scolia.Domain.Deliberation;
using Scolia.Domain.Students;

namespace Scolia.Domain.School;

/// <summary>
/// Trace pédagogique propre à un cycle. Ne remplace ni les cotes ITM/IM,
/// ni les épreuves du jury, ni les crédits du LMD.
/// </summary>
public sealed class SchoolCycleAssessment : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid SchoolCycleId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid SchoolCyclePeriodId { get; set; }
    /// <summary>Matière annuelle de la classe ; facultative pour les observations maternelles.</summary>
    public Guid? CurriculumItemId { get; set; }
    /// <summary>Identifiant stable de l'activité ou de l'évaluation dans cette période.</summary>
    public string EvaluationCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public SchoolCycleAssessmentMode Mode { get; set; }
    /// <summary>Récit d'observation du développement ; pas un score numérique.</summary>
    public string? Observation { get; set; }
    /// <summary>Note uniquement pour primaire / secondaire. Absence non cotée = null.</summary>
    public decimal? Score { get; set; }
    public decimal? MaxScore { get; set; }
    public string? Comment { get; set; }
}

/// <summary>
/// Aperçu de bulletin : volontairement NON officiel, sans moyenne et sans
/// classement tant que les règles ministérielles et celles de l'école
/// ne sont pas intégrées et validées.
/// </summary>
public sealed record SchoolCycleAssessmentPreviewLine(
    string PeriodCode, string EvaluationCode, string Title,
    string? Observation, decimal? Score, decimal? MaxScore, string? Comment);

public static class SchoolCycleAssessmentRules
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException("evaluationCycle", message);
    }

    /// <summary>
    /// Contrôle un enregistrement à partir des entités déjà validées par le
    /// service : empêche toute erreur de cycle, classe ou période.
    /// </summary>
    public static void Validate(
        SchoolCycle cycle,
        SchoolCyclePeriod period,
        Promotion promotion,
        Enrollment enrollment,
        CurriculumItem? subject,
        SchoolCycleAssessment entry)
    {
        Require(cycle.IsActive, "Le cycle scolaire est inactif.");
        Require(entry.SchoolCycleId == cycle.Id &&
                promotion.SchoolCycleId == cycle.Id &&
                period.SchoolCycleId == cycle.Id,
            "La période, la classe et l'évaluation doivent appartenir au même cycle.");
        Require(entry.AcademicYearId == promotion.AcademicYearId &&
                entry.AcademicYearId == enrollment.AcademicYearId &&
                entry.AcademicYearId == period.AcademicYearId,
            "L'évaluation ne peut pas mélanger deux années scolaires.");
        Require(entry.PromotionId == promotion.Id &&
                entry.EnrollmentId == enrollment.Id &&
                enrollment.PromotionId == promotion.Id,
            "L'évaluation doit concerner un élève inscrit dans cette classe.");
        Require(entry.SchoolCyclePeriodId == period.Id, "La période d'évaluation est incorrecte.");
        Require(entry.Mode == period.Mode, "Le mode d'évaluation doit correspondre au cycle et à la période.");
        Require(!string.IsNullOrWhiteSpace(entry.EvaluationCode) && entry.EvaluationCode.Length <= 24 &&
                entry.EvaluationCode.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'),
            "Le code d'évaluation doit comporter 1 à 24 caractères, lettres, chiffres, - ou _.");
        Require(!string.IsNullOrWhiteSpace(entry.Title) && entry.Title.Length <= 160,
            "La désignation de l'évaluation est obligatoire (160 caractères maximum).");
        Require(entry.Comment is null || entry.Comment.Length <= 800,
            "Le commentaire doit contenir au maximum 800 caractères.");

        if (cycle.Kind == SchoolCycleKind.Maternelle)
        {
            Require(period.Mode == SchoolCycleAssessmentMode.Observation,
                "La maternelle utilise une observation descriptive, jamais une cote chiffrée automatique.");
            Require(!string.IsNullOrWhiteSpace(entry.Observation) &&
                    entry.Observation.Length is <= 800,
                "L'observation maternelle est obligatoire (800 caractères maximum).");
            Require(entry.Score is null && entry.MaxScore is null,
                "Aucune note ni maxima chiffrés ne sont autorisés pour cette observation maternelle.");
            if (subject is not null)
                Require(entry.CurriculumItemId == subject.Id &&
                        subject.PromotionId == promotion.Id &&
                        subject.AcademicYearId == entry.AcademicYearId &&
                        subject.Kind == CurriculumItemKind.AnnualSubject &&
                        subject.IsActive,
                    "L'activité maternelle doit appartenir au programme actif de cette classe.");
            else
                Require(entry.CurriculumItemId is null, "Activité pédagogique introuvable.");
            return;
        }

        Require(cycle.Kind is SchoolCycleKind.Primaire or SchoolCycleKind.Secondaire,
            "Seuls les cycles maternel, primaire et secondaire utilisent ce module.");
        Require(entry.Mode == SchoolCycleAssessmentMode.Evaluation,
            "Une école primaire ou secondaire utilise une évaluation de période.");
        Require(string.IsNullOrWhiteSpace(entry.Observation),
            "Une observation descriptive de maternelle ne doit pas être transformée en cote scolaire.");
        Require(entry.CurriculumItemId is not null && subject is not null &&
                subject.Id == entry.CurriculumItemId &&
                subject.PromotionId == promotion.Id &&
                subject.AcademicYearId == entry.AcademicYearId &&
                subject.Kind == CurriculumItemKind.AnnualSubject &&
                subject.Semester == CourseSemester.Annuel && subject.IsActive,
            "Une cote doit être associée à une matière annuelle active de cette classe.");
        Require(entry.MaxScore is > 0 and <= 1000,
            "Le maximum de l'évaluation doit être strictement positif et ne peut dépasser 1000.");
        Require(entry.Score is null || entry.Score >= 0 && entry.Score <= entry.MaxScore,
            "La cote doit être comprise entre zéro et le maximum déclaré.");
    }

    /// <summary>
    /// Données ordonnées pour un futur carnet / bulletin, sans calcul de
    /// moyenne annuelle, décision de réussite, classement ni seuil inventé.
    /// </summary>
    public static IReadOnlyList<SchoolCycleAssessmentPreviewLine> Preview(
        SchoolCycle cycle, Guid enrollmentId,
        IReadOnlyCollection<SchoolCyclePeriod> periods,
        IReadOnlyCollection<SchoolCycleAssessment> assessments,
        Guid? expectedPromotionId = null, Guid? expectedAcademicYearId = null)
    {
        var byPeriod = periods.ToDictionary(p => p.Id);
        // Le même code d'interrogation est réutilisable dans plusieurs matières ;
        // l'unicité est définie par élève (paramètre), période, matière et code.
        var unique = new HashSet<(Guid PeriodId, Guid? SubjectId, string Code)>();
        var entries = new List<(int Order, SchoolCycleAssessmentPreviewLine Line)>();
        foreach (var entry in assessments)
        {
            Require(entry.EnrollmentId == enrollmentId && entry.SchoolCycleId == cycle.Id,
                "Le bulletin ne doit contenir que les évaluations de cet élève et de son cycle.");
            Require((expectedPromotionId is null || entry.PromotionId == expectedPromotionId) &&
                    (expectedAcademicYearId is null || entry.AcademicYearId == expectedAcademicYearId),
                "Une évaluation d'une autre classe ou année ne peut pas figurer au bulletin.");
            Require(byPeriod.TryGetValue(entry.SchoolCyclePeriodId, out var period) &&
                    period.SchoolCycleId == cycle.Id && period.AcademicYearId == entry.AcademicYearId,
                "Période étrangère dans l'aperçu du bulletin.");
            Require(unique.Add((period!.Id, entry.CurriculumItemId, entry.EvaluationCode.ToUpperInvariant())),
                "Deux évaluations de la même matière portant le même code ne peuvent figurer dans la même période.");
            Require(entry.Mode == period.Mode, "Évaluation incohérente avec la période.");
            // Ne jamais transformer une observation maternelle conservée en
            // base en note numérique, même dans un aperçu ancien.
            if (cycle.Kind == SchoolCycleKind.Maternelle)
                Require(period.Mode == SchoolCycleAssessmentMode.Observation &&
                        entry.Observation is { Length: > 0 } &&
                        entry.Score is null && entry.MaxScore is null,
                    "Une observation maternelle ne peut pas être traitée comme une cote numérique.");
            else
                Require(cycle.Kind is SchoolCycleKind.Primaire or SchoolCycleKind.Secondaire &&
                        period.Mode == SchoolCycleAssessmentMode.Evaluation &&
                        string.IsNullOrWhiteSpace(entry.Observation) &&
                        entry.MaxScore is > 0,
                    "Un relevé de notes doit rester distinct du carnet d'observations maternelles.");
            entries.Add((period.Order,
                new(period.Code, entry.EvaluationCode, entry.Title,
                    entry.Observation, entry.Score, entry.MaxScore, entry.Comment)));
        }
        return entries.OrderBy(e => e.Order).ThenBy(e => e.Line.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => e.Line).ToArray();
    }
}
