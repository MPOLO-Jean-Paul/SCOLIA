using Scolia.Domain.Common;

namespace Scolia.Domain.Deliberation;

/// <summary>
/// Moteur de délibération unique, repris des règles d'ITMCD (<c>config/db.php</c>) et utilisé par tous les écrans
/// et documents : encodage, délibération, grille, palmarès, procès-verbal.
/// <list type="bullet">
/// <item>Cote d'une période : la cote saisie pour ce semestre, sur le maxima du cours.</item>
/// <item>Cours annuel au résultat final : moyenne des cotes du S1 et du S2 ; il faut les deux (sinon cote manquante).</item>
/// <item>Moyenne d'appui m/10 : si la cote ne dépasse pas 75 % du maxima, on ajoute m/10 × 25 % du maxima, sans dépasser le maxima.</item>
/// <item>Cours en échec : points inférieurs à la moitié du maxima.</item>
/// <item>Admis : au moins 60 % du total des maxima et aucune cote manquante ; Incomplet s'il manque une cote ; sinon Ajourné.</item>
/// <item>Mentions : A dès 90 %, B dès 80 %, C dès 70 %, D dès 60 %, E en dessous.</item>
/// </list>
/// </summary>
public static class DeliberationEngine
{
    public const decimal DefaultPassPercent = 60m;
    public const decimal SupportScale = 10m;
    /// <summary>Au-delà de 75 % du maxima, la moyenne d'appui ne change rien.</summary>
    public const decimal SupportCeilingRatio = 0.75m;
    /// <summary>L'appui apporte au plus 25 % du maxima.</summary>
    public const decimal SupportShareRatio = 0.25m;

    public sealed record CourseInput(Guid CourseId, CourseSemester Semester, decimal Max, decimal? S1, decimal? S2, decimal? Support, decimal? Jury = null);

    /// <summary>Épreuve du jury (projet Délibération) : comptée au seul jury annuel, cotée par le jury.</summary>
    public static bool IsJury(CourseSemester semester) => semester is CourseSemester.JuryEpreuve or CourseSemester.JuryBloc;

    /// <param name="Base">Cote retenue avant l'appui (moyenne S1/S2 pour un cours annuel au final).</param>
    /// <param name="Bonus">Points apportés par la moyenne d'appui.</param>
    public sealed record CourseResult(Guid CourseId, decimal Max, decimal? Base, decimal Bonus, decimal Points, bool Graded, bool Failed);

    public sealed record StudentResult(IReadOnlyList<CourseResult> Courses, decimal Points, decimal Max, decimal Percent, int Missing, int Failed, Outcome Outcome, string Mention);

    /// <summary>Le cours compte-t-il pour cette période ? S1 : cours S1 et annuels ; S2 : cours S2 et annuels ; jury annuel : tous.</summary>
    public static bool Includes(CourseSemester semester, DeliberationPeriod period) => IsJury(semester) ? period == DeliberationPeriod.Annuel : period switch
    {
        DeliberationPeriod.S1 => semester is CourseSemester.S1 or CourseSemester.Annuel,
        DeliberationPeriod.S2 => semester is CourseSemester.S2 or CourseSemester.Annuel,
        _ => true,
    };

    /// <summary>Natures de cote qu'un cours accepte : un cours S1 ne reçoit qu'une cote S1, un cours S2 qu'une cote S2, un annuel les deux.</summary>
    public static bool Accepts(CourseSemester semester, MarkKind kind) => kind switch
    {
        MarkKind.CoteS1 => semester is CourseSemester.S1 or CourseSemester.Annuel,
        MarkKind.CoteS2 => semester is CourseSemester.S2 or CourseSemester.Annuel,
        MarkKind.Appui => !IsJury(semester),
        MarkKind.Jury => IsJury(semester),
        _ => false,
    };

    /// <summary>Cote de base de la période, avant appui ; vide si elle manque.</summary>
    public static decimal? BaseScore(CourseInput course, DeliberationPeriod period) => IsJury(course.Semester) ? course.Jury : period switch
    {
        DeliberationPeriod.S1 => course.S1,
        DeliberationPeriod.S2 => course.S2,
        _ => course.Semester switch
        {
            CourseSemester.S1 => course.S1,
            CourseSemester.S2 => course.S2,
            _ => course.S1 is { } s1 && course.S2 is { } s2 ? Round((s1 + s2) / 2) : null,
        },
    };

    /// <summary>Points apportés par la moyenne d'appui (0 si la cote dépasse 75 % du maxima).</summary>
    public static decimal SupportBonus(decimal basePoints, decimal? support, decimal max)
    {
        if (support is not { } m || max <= 0) return 0;
        var clampedBase = Math.Clamp(basePoints, 0, max);
        if (clampedBase > max * SupportCeilingRatio) return 0;
        var bonus = Math.Clamp(m, 0, SupportScale) / SupportScale * (max * SupportShareRatio);
        return Round(Math.Min(max, clampedBase + bonus) - clampedBase);
    }

    public static CourseResult Course(CourseInput course, DeliberationPeriod period, bool supportEnabled = true)
    {
        var baseScore = BaseScore(course, period);
        if (baseScore is not { } b || course.Max <= 0)
            return new CourseResult(course.CourseId, course.Max, null, 0, 0, false, false);
        var clamped = Math.Clamp(b, 0, course.Max);
        var bonus = supportEnabled && !IsJury(course.Semester) ? SupportBonus(clamped, course.Support, course.Max) : 0;
        var points = Round(clamped + bonus);
        return new CourseResult(course.CourseId, course.Max, clamped, bonus, points, true, points < course.Max / 2);
    }

    public static StudentResult Student(IEnumerable<CourseInput> courses, DeliberationPeriod period, bool supportEnabled = true, decimal passPercent = DefaultPassPercent)
    {
        var results = courses.Where(c => Includes(c.Semester, period)).Select(c => Course(c, period, supportEnabled)).ToList();
        var points = results.Sum(r => r.Points);
        var max = results.Sum(r => r.Max);
        var percent = max > 0 ? Math.Round(points / max * 100, 2, MidpointRounding.AwayFromZero) : 0;
        var missing = results.Count(r => !r.Graded);
        var failed = results.Count(r => r.Failed);
        var outcome = max <= 0 || missing > 0 ? Outcome.Incomplet : percent >= passPercent ? Outcome.Admis : Outcome.Ajourne;
        return new StudentResult(results, points, max, percent, missing, failed, outcome, Mention(percent));
    }

    public static string Mention(decimal percent) => percent switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "E",
    };

    /// <summary>
    /// Décision officielle : celle du jury si elle existe, sinon le résultat calculé.
    /// À la clôture, un dossier encore incomplet sans décision du jury est ajourné (règle d'ITMCD).
    /// </summary>
    public static Outcome Final(Outcome computed, Outcome? jury, bool closing) =>
        jury ?? (closing && computed == Outcome.Incomplet ? Outcome.Ajourne : computed);

    /// <summary>Rang dans la promotion par pourcentage décroissant, ex æquo au même rang ; les dossiers incomplets ne sont pas classés.</summary>
    public static IReadOnlyDictionary<Guid, int> Ranks(IEnumerable<(Guid Id, decimal Percent, bool Complete)> rows)
    {
        var ranked = rows.Where(r => r.Complete).OrderByDescending(r => r.Percent).ToList();
        var ranks = new Dictionary<Guid, int>();
        for (var i = 0; i < ranked.Count; i++)
            ranks[ranked[i].Id] = i > 0 && ranked[i].Percent == ranked[i - 1].Percent ? ranks[ranked[i - 1].Id] : i + 1;
        return ranks;
    }

    public static string Label(Outcome outcome) => outcome switch
    {
        Outcome.Admis => "Admis",
        Outcome.Incomplet => "Incomplet",
        _ => "Ajourné",
    };

    /// <summary>Libellé de la décision finale ; « par délibération » quand le jury s'écarte du calcul.</summary>
    public static string DecisionLabel(Outcome computed, Outcome final, bool juryDecided) =>
        juryDecided && final != computed ? $"{Label(final)} par délibération" : Label(final);

    public static string PeriodLabel(DeliberationPeriod period) => period switch
    {
        DeliberationPeriod.S1 => "Premier semestre",
        DeliberationPeriod.S2 => "Second semestre",
        _ => "Jury annuel",
    };

    /// <summary>Contrôle une cote avant enregistrement : 0 à maxima (ou 0 à 10 pour l'appui), deux décimales au plus.</summary>
    public static decimal ValidateMark(MarkKind kind, CourseSemester semester, decimal value, decimal max)
    {
        if (!Accepts(semester, kind))
            throw new DomainException("cote", IsJury(semester)
                ? "Cette épreuve du jury ne reçoit que la cote du jury."
                : kind == MarkKind.Jury ? "Seules les épreuves du jury reçoivent une cote du jury."
                : semester == CourseSemester.S1
                ? "Ce cours est du premier semestre : il ne reçoit pas de cote du second semestre."
                : "Ce cours est du second semestre : il ne reçoit pas de cote du premier semestre.");
        if (decimal.Round(value, 2) != value) throw new DomainException("cote", "Une cote accepte au maximum deux décimales.");
        var limit = kind == MarkKind.Appui ? SupportScale : max;
        if (value < 0 || value > limit)
            throw new DomainException("cote", kind == MarkKind.Appui
                ? "La moyenne d'appui doit être comprise entre 0 et 10."
                : $"La cote doit être comprise entre 0 et {limit:0.##}.");
        return value;
    }

    /// <summary>Natures de cote touchées par la clôture d'une période : le jury annuel fige tout.</summary>
    public static bool Locks(DeliberationPeriod closed, MarkKind kind, CourseSemester semester) => kind switch
    {
        MarkKind.CoteS1 => closed is DeliberationPeriod.S1 or DeliberationPeriod.Annuel,
        MarkKind.CoteS2 => closed is DeliberationPeriod.S2 or DeliberationPeriod.Annuel,
        MarkKind.Jury => closed == DeliberationPeriod.Annuel,
        // L'appui sert à toutes les périodes du cours : il est figé dès qu'une période de ce cours est close.
        _ => Includes(semester, closed),
    };

    public static MarkKind KindFor(DeliberationPeriod period) => period == DeliberationPeriod.S2 ? MarkKind.CoteS2 : MarkKind.CoteS1;

    /// <summary>
    /// Code officiel du jury annuel (projet Délibération) : P — réussite dans tous les cours ; A — moyenne atteinte avec
    /// au moins un cours en échec ; E — moyenne inférieure au seuil. Vide tant qu'il manque une cote.
    /// </summary>
    public static string Grade(Outcome final, decimal percent, int failed, decimal passPercent = DefaultPassPercent) => final switch
    {
        Outcome.Admis => failed == 0 ? "P" : "A",
        _ when percent < passPercent => "E",
        _ => string.Empty,
    };

    public static string GradeLabel(string grade) => grade switch
    {
        "P" => "P — réussite dans tous les cours",
        "A" => "A — moyenne atteinte, au moins un cours en échec",
        "E" => "E — moyenne inférieure au seuil",
        _ => string.Empty,
    };

    /// <summary>Totaux officiels du projet Délibération : TOTAL 1 (épreuves intégratives), SOUS-TOTAL 1 (cours), SOUS-TOTAL 2 (blocs).</summary>
    public sealed record Breakdown(decimal Total1, decimal Total1Max, decimal Subtotal1, decimal Subtotal1Max, decimal Subtotal2, decimal Subtotal2Max)
    {
        public decimal Total2 => Subtotal1 + Subtotal2;
        public decimal Total2Max => Subtotal1Max + Subtotal2Max;
        public decimal General => Total1 + Total2;
        public decimal GeneralMax => Total1Max + Total2Max;
    }

    public static Breakdown Totals(IEnumerable<(CourseSemester Semester, CourseResult Result)> courses)
    {
        decimal t1 = 0, t1m = 0, s1 = 0, s1m = 0, s2 = 0, s2m = 0;
        foreach (var (semester, r) in courses)
        {
            switch (semester)
            {
                case CourseSemester.JuryEpreuve: t1 += r.Points; t1m += r.Max; break;
                case CourseSemester.JuryBloc: s2 += r.Points; s2m += r.Max; break;
                default: s1 += r.Points; s1m += r.Max; break;
            }
        }
        return new Breakdown(t1, t1m, s1, s1m, s2, s2m);
    }

    /// <summary>
    /// Épreuves du jury standard du projet Délibération (ITM) : nom, maxima, coefficient et organisation.
    /// Créées à la demande dans une promotion, elles restent modifiables comme tout cours.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, decimal Max, decimal Coefficient, CourseSemester Semester)> StandardJuryEvaluations =
    [
        ("Stage", 150, 15, CourseSemester.JuryEpreuve),
        ("Situation d'intégration", 50, 5, CourseSemester.JuryEpreuve),
        ("Dossier d'évaluation", 90, 9, CourseSemester.JuryEpreuve),
        ("Épreuve d'intégration", 225, 22.5m, CourseSemester.JuryEpreuve),
        ("Bloc prévention", 60, 6, CourseSemester.JuryBloc),
        ("Bloc diagnostic", 50, 5, CourseSemester.JuryBloc),
        ("Bloc gestion", 50, 5, CourseSemester.JuryBloc),
    ];

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
