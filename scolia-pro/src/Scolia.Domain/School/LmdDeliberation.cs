using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>Note d'un étudiant à un EC pour une session (1 ou 2).</summary>
public sealed class LmdMark : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid PromotionId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Guid ElementId { get; set; }
    public int Session { get; set; } = 1;
    public decimal? Score { get; set; }
}

public enum LmdSessionStatus
{
    Ouverte = 0,
    /// <summary>Résultats partiels du premier semestre publiés ; cotes du S1 figées.</summary>
    S1Publie = 1,
    /// <summary>Résultats de la première session publiés ; deuxième session ouverte.</summary>
    PremiereSessionCloturee = 2,
    /// <summary>Deuxième session clôturée : décisions définitives de l'année.</summary>
    Cloturee = 3,
}

/// <summary>Délibération annuelle d'une promotion LMD, distincte des sessions ITM.</summary>
public sealed class LmdDeliberation : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid PromotionId { get; set; }
    public LmdSessionStatus Status { get; set; } = LmdSessionStatus.Ouverte;
    public string? S1PublishedBy { get; set; }
    public DateTime? S1PublishedAtUtc { get; set; }
    public string? FirstSessionClosedBy { get; set; }
    public DateTime? FirstSessionClosedAtUtc { get; set; }
    public string? ClosedBy { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>Résultats publiés d'un étudiant à chaque étape de l'année.</summary>
public sealed class LmdDecision : Entity
{
    public Guid DeliberationId { get; set; }
    public Guid EnrollmentId { get; set; }
    public decimal? S1Average { get; set; }
    public decimal S1Credits { get; set; }
    public decimal? FirstAverage { get; set; }
    public decimal FirstCredits { get; set; }
    public LmdOutcome? FirstOutcome { get; set; }
    public LmdOutcome? Jury { get; set; }
    public string? JuryReason { get; set; }
    /// <summary>Décision motivée du jury après la deuxième session.</summary>
    public LmdOutcome? FinalJury { get; set; }
    public string? FinalJuryReason { get; set; }
    public decimal? FinalAverage { get; set; }
    public decimal FinalCredits { get; set; }
    public LmdOutcome? Final { get; set; }
    public int? Rank { get; set; }

    public LmdOutcome? Effective => Final ?? FirstOutcome;
}

/// <summary>UE non acquise reportée à l'année suivante (passage avec dettes).</summary>
public sealed class LmdDebt : Entity
{
    public Guid StudentId { get; set; }
    public Guid OriginEnrollmentId { get; set; }
    public Guid OriginYearId { get; set; }
    public Guid UeId { get; set; }
    public string UeCode { get; set; } = string.Empty;
    public string UeName { get; set; } = string.Empty;
    public string Semester { get; set; } = string.Empty;
    public decimal Credits { get; set; }
    public decimal? ClearedMark { get; set; }
    public string? ClearedBy { get; set; }
    public DateTime? ClearedAtUtc { get; set; }

    public bool IsCleared => ClearedAtUtc is not null;
}

public static class LmdDeliberationRules
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException("deliberationLmd", message);
    }

    /// <summary>Niveau d'étude : 1 à 3 = licence (L1 à L3), 4 et 5 = master (M1, M2).</summary>
    public static string LevelLabel(int level) => level switch
    {
        >= 1 and <= 3 => $"L{level}",
        4 => "M1",
        5 => "M2",
        _ => $"Niveau {level}",
    };

    public static void ValidateJury(LmdOutcome? jury, string? reason)
    {
        if (jury is null) return;
        Require(jury is LmdOutcome.Admis or LmdOutcome.DeuxiemeSession or LmdOutcome.AdmisAvecDettes or LmdOutcome.Ajourne,
            "Le jury décide : admis, deuxième session, admis avec dettes ou ajourné.");
        Require(!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length is >= 5 and <= 300,
            "Le motif de la décision du jury est obligatoire (5 à 300 caractères).");
    }

    /// <summary>
    /// Une note de première session du S1 se saisit jusqu'à la publication du S1 ; celle du S2,
    /// jusqu'à la clôture de la première session. La deuxième session s'ouvre ensuite.
    /// </summary>
    public static void EnsureMarkEditable(LmdSessionStatus status, Deliberation.CourseSemester semester, int session)
    {
        Require(session is 1 or 2, "Session inconnue : 1 ou 2.");
        if (session == 1)
        {
            Require(status == LmdSessionStatus.Ouverte ||
                    status == LmdSessionStatus.S1Publie && semester == Deliberation.CourseSemester.S2,
                semester == Deliberation.CourseSemester.S1 && status != LmdSessionStatus.Ouverte
                    ? "Les résultats du premier semestre sont publiés : ses notes sont figées."
                    : "La première session est clôturée : ses notes sont figées.");
            return;
        }
        Require(status == LmdSessionStatus.PremiereSessionCloturee,
            status == LmdSessionStatus.Cloturee
                ? "La deuxième session est clôturée."
                : "La deuxième session s'ouvre après la publication des résultats de la première session.");
    }
}
