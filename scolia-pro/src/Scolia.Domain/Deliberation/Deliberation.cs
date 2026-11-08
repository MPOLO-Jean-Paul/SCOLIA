using Scolia.Domain.Common;

namespace Scolia.Domain.Deliberation;

/// <summary>
/// Organisation d'un cours dans l'année (repris d'ITMCD : S1, S2 ou ANNUEL), et épreuves du jury reprises du projet
/// Délibération : elles ne comptent qu'au jury annuel et sont cotées par le jury seul. Les épreuves intégratives
/// (stage, situation d'intégration, dossier, épreuve d'intégration) forment le TOTAL 1 ; les blocs (prévention,
/// diagnostic, gestion) le SOUS-TOTAL 2. Valeurs persistées : ne pas renuméroter.
/// </summary>
public enum CourseSemester { Annuel = 0, S1 = 1, S2 = 2, JuryEpreuve = 3, JuryBloc = 4 }

/// <summary>Période délibérée : un semestre, ou le jury annuel (résultat final de l'année). Valeurs persistées.</summary>
public enum DeliberationPeriod { S1 = 1, S2 = 2, Annuel = 3 }

/// <summary>Nature d'une cote enregistrée pour la délibération. Valeurs persistées.</summary>
public enum MarkKind
{
    /// <summary>Cote du premier semestre, sur le maxima du cours.</summary>
    CoteS1 = 1,
    /// <summary>Cote du second semestre, sur le maxima du cours.</summary>
    CoteS2 = 2,
    /// <summary>Moyenne d'appui des travaux, sur 10, qui soutient une cote faible.</summary>
    Appui = 3,
    /// <summary>Cote d'une épreuve du jury (projet Délibération), sur le maxima de l'épreuve.</summary>
    Jury = 4,
}

/// <summary>
/// Code anonyme officiel d'un apprenant pour l'année : il figure sur les copies et sert à l'encodage des cotes
/// sans révéler le nom. Un seul code actif par inscription ; un code n'est jamais réutilisé dans l'année,
/// même après remplacement, pour qu'une ancienne copie ne puisse pas être attribuée à quelqu'un d'autre.
/// </summary>
public sealed class AnonymousCode : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid EnrollmentId { get; set; }
    /// <summary>Code normalisé (majuscules, sans espaces), par exemple « P13W5R ».</summary>
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>Vrai pour un code saisi à la main (code officiel déjà utilisé, par exemple dans ITMCD).</summary>
    public bool IsManual { get; set; }
    public string? ReplacedReason { get; set; }
    public DateTime? ReplacedAtUtc { get; set; }
}

/// <summary>Cote d'un apprenant pour un cours : cote du S1, cote du S2 ou moyenne d'appui. Unique par inscription, cours et nature.</summary>
public sealed class CourseMark : Entity
{
    public Guid EnrollmentId { get; set; }
    public Guid CourseId { get; set; }
    public MarkKind Kind { get; set; }
    public decimal Value { get; set; }
    /// <summary>Vrai quand la cote a été encodée par code anonyme, sans afficher le nom de l'apprenant.</summary>
    public bool EnteredAnonymously { get; set; }
}

/// <summary>
/// Remise officielle des cotes d'un cours pour une nature (S1, S2 ou appui), comme le « dépôt » des cotes d'ITMCD.
/// Après la remise, la feuille ne change plus que par le jury (correction tracée au journal) ; le jury peut aussi
/// renvoyer la feuille à l'enseignant, avec un motif, ce qui supprime la remise.
/// </summary>
public sealed class MarkSubmission : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid CourseId { get; set; }
    public MarkKind Kind { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public string SubmittedByName { get; set; } = string.Empty;
}

/// <summary>Réglages de délibération d'une année : ouverture de l'encodage par semestre et moyennes d'appui (interrupteurs d'ITMCD).</summary>
public sealed class DeliberationSettings : Entity
{
    public Guid AcademicYearId { get; set; }
    public bool EncodingS1Open { get; set; } = true;
    public bool EncodingS2Open { get; set; } = true;
    /// <summary>Les moyennes d'appui sont saisies et prises en compte dans le calcul.</summary>
    public bool SupportEnabled { get; set; } = true;
    /// <summary>Seuil de réussite en pourcentage (60 dans ITMCD).</summary>
    public decimal PassPercent { get; set; } = DeliberationEngine.DefaultPassPercent;
    /// <summary>
    /// « Mise à jour des cotes » du projet Délibération : un enseignant peut remplacer ou effacer une cote déjà enregistrée.
    /// Fermé par défaut : l'enseignant ajoute ses cotes, le secrétariat corrige.
    /// </summary>
    public bool TeacherUpdates { get; set; }
    /// <summary>« Téléchargements » du projet Délibération : l'enseignant exporte le relevé et la liste des échecs de ses cours.</summary>
    public bool TeacherDownloads { get; set; } = true;

    // Autorités (projet Délibération) : elles signent le bulletin, la grille et le procès-verbal.
    public string? Prefect { get; set; }
    public string? Principal { get; set; }
    public string? JuryPresident { get; set; }
    public string? JurySecretary { get; set; }
    /// <summary>Membres du jury, un par ligne (quatre au plus).</summary>
    public string? JuryMembers { get; set; }
}

public enum SessionStatus { Ouverte = 0, Cloturee = 1 }

/// <summary>
/// Session de délibération d'une promotion pour une période : le jury examine, décide, puis clôture.
/// La clôture fige les résultats (instantané) et verrouille les cotes concernées ; une réouverture exige un motif.
/// </summary>
public sealed class DeliberationSession : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid PromotionId { get; set; }
    public DeliberationPeriod Period { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Ouverte;
    public string? President { get; set; }
    public string? Members { get; set; }
    public string? Notes { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? ClosedByName { get; set; }
    public int ReopenCount { get; set; }
    public string? LastReopenReason { get; set; }

    public bool IsClosed => Status == SessionStatus.Cloturee;
}

/// <summary>Décision motivée du jury pour un apprenant : elle remplace le résultat calculé sans jamais écraser une cote.</summary>
public sealed class JuryDecision : Entity
{
    public Guid SessionId { get; set; }
    public Guid EnrollmentId { get; set; }
    public Outcome Outcome { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Résultat figé à la clôture d'une session : ce qui figure au procès-verbal.</summary>
public sealed class DeliberationResult : Entity
{
    public Guid SessionId { get; set; }
    public Guid EnrollmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Matricule { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal Points { get; set; }
    public decimal MaxPoints { get; set; }
    public decimal Percent { get; set; }
    public int Missing { get; set; }
    public int FailedCourses { get; set; }
    public Outcome Computed { get; set; }
    public Outcome Final { get; set; }
    public string Mention { get; set; } = string.Empty;
    public int? Rank { get; set; }
    public string? Reason { get; set; }
    /// <summary>Code imprimé (et en QR) sur le bulletin officiel : il permet de vérifier le résultat ; il change à chaque clôture.</summary>
    public string VerificationCode { get; set; } = string.Empty;
}

/// <summary>Résultat d'un apprenant. Incomplet : il manque au moins une cote (pendant l'encodage).</summary>
public enum Outcome { Ajourne = 0, Admis = 1, Incomplet = 2 }
