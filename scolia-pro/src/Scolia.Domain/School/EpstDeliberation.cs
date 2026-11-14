using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>Décision de fin d'année du primaire ou du secondaire. Valeurs persistées : ne pas renuméroter.</summary>
public enum EpstOutcome
{
    Incomplet = 0,
    Passe = 1,
    Repechage = 2,
    Redouble = 3,
    PasseApresRepechage = 4,
    Reoriente = 5,
}

public enum EpstSessionStatus
{
    Ouverte = 0,
    /// <summary>Première session clôturée : décisions publiées, repêchage ouvert.</summary>
    PremiereSessionCloturee = 1,
    /// <summary>Repêchage clôturé : décisions définitives de l'année.</summary>
    Cloturee = 2,
}

/// <summary>Délibération de fin d'année d'une classe EPST.</summary>
public sealed class EpstDeliberation : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid PromotionId { get; set; }
    public EpstSessionStatus Status { get; set; } = EpstSessionStatus.Ouverte;
    public string? FirstSessionClosedBy { get; set; }
    public DateTime? FirstSessionClosedAtUtc { get; set; }
    public string? ClosedBy { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>Décision d'un élève : calcul, éventuelle décision du jury, issue du repêchage.</summary>
public sealed class EpstDecision : Entity
{
    public Guid DeliberationId { get; set; }
    public Guid EnrollmentId { get; set; }
    /// <summary>Résultat de la première session au moment de la clôture.</summary>
    public EpstOutcome Computed { get; set; }
    public decimal? Percent { get; set; }
    public int? Rank { get; set; }
    /// <summary>Décision du jury qui remplace le calcul, avec motif obligatoire.</summary>
    public EpstOutcome? Jury { get; set; }
    public string? JuryReason { get; set; }
    /// <summary>Branches présentées au repêchage (identifiants séparés par des virgules).</summary>
    public string RepechageSubjects { get; set; } = string.Empty;
    /// <summary>Issue définitive après la seconde session ; absente avant sa clôture.</summary>
    public EpstOutcome? Final { get; set; }

    public IReadOnlyList<Guid> RepechageSubjectIds =>
        RepechageSubjects.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();

    public EpstOutcome Effective => Final ?? Jury ?? Computed;
}

/// <summary>Points obtenus à l'épreuve de repêchage d'une branche.</summary>
public sealed class EpstRepechageMark : Entity
{
    public Guid DeliberationId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid SubjectId { get; set; }
    public decimal? Score { get; set; }
    public decimal MaxScore { get; set; }
}

public static class EpstDeliberationRules
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException("deliberationEpst", message);
    }

    public static string Label(EpstOutcome outcome) => outcome switch
    {
        EpstOutcome.Passe => "Passe",
        EpstOutcome.Repechage => "Repêchage",
        EpstOutcome.Redouble => "Double",
        EpstOutcome.PasseApresRepechage => "Passe après repêchage",
        EpstOutcome.Reoriente => "Réorienté",
        _ => "Incomplet",
    };

    /// <summary>Passe à la classe supérieure à l'issue de l'année.</summary>
    public static bool Promotes(EpstOutcome outcome) =>
        outcome is EpstOutcome.Passe or EpstOutcome.PasseApresRepechage;

    /// <summary>
    /// Décision de première session selon le règlement validé : sous le seuil global,
    /// l'élève double ; au-dessus, il passe sans échec, va au repêchage s'il a au plus
    /// le nombre de branches en échec autorisé, double sinon.
    /// </summary>
    public static (EpstOutcome Outcome, IReadOnlyList<Guid> RepechageSubjects) FirstSession(
        AcademicRegulation regulation, EpstPupilResult pupil)
    {
        Require(regulation.IsValidated && AcademicRegulationRules.IsEpst(regulation.Scope),
            "Le règlement EPST validé de l'année est requis.");
        if (!pupil.Complete || pupil.Percent is null) return (EpstOutcome.Incomplet, []);
        if (pupil.Percent < regulation.PassPercent) return (EpstOutcome.Redouble, []);
        var failed = pupil.Subjects.Where(s => s.Failed).Select(s => s.SubjectId).ToList();
        if (failed.Count == 0) return (EpstOutcome.Passe, []);
        return failed.Count <= regulation.MaxRepechageSubjects
            ? (EpstOutcome.Repechage, failed)
            : (EpstOutcome.Redouble, []);
    }

    /// <summary>Le jury tranche : passer, doubler ou réorienter, toujours avec un motif.</summary>
    public static void ValidateJury(EpstOutcome? jury, string? reason)
    {
        if (jury is null) return;
        Require(jury is EpstOutcome.Passe or EpstOutcome.Redouble or EpstOutcome.Reoriente or EpstOutcome.Repechage,
            "Le jury décide : passe, repêchage, double ou réorienté.");
        Require(!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length is >= 5 and <= 300,
            "Le motif de la décision du jury est obligatoire (5 à 300 caractères).");
    }

    /// <summary>Issue du repêchage : chaque branche présentée doit atteindre le seuil de branche.</summary>
    public static EpstOutcome AfterRepechage(AcademicRegulation regulation, IReadOnlyList<Guid> subjects,
        IReadOnlyCollection<EpstRepechageMark> marks)
    {
        Require(subjects.Count > 0, "Aucune branche n'est présentée au repêchage.");
        foreach (var subject in subjects)
        {
            var mark = marks.FirstOrDefault(m => m.SubjectId == subject);
            Require(mark is { Score: not null, MaxScore: > 0 },
                "Les points de repêchage de chaque branche présentée doivent être saisis.");
            if (mark!.Score!.Value / mark.MaxScore * 100 < regulation.SubjectPassPercent)
                return EpstOutcome.Redouble;
        }
        return EpstOutcome.PasseApresRepechage;
    }

    public static void ValidateMark(EpstRepechageMark mark)
    {
        Require(mark.MaxScore is > 0 and <= 1000, "Le maximum de l'épreuve de repêchage est compris entre 1 et 1000.");
        Require(mark.Score is null || mark.Score >= 0 && mark.Score <= mark.MaxScore,
            "Les points de repêchage sont compris entre zéro et le maximum.");
    }
}
