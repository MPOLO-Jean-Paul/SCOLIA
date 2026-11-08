namespace Scolia.Application.Dtos;

// ---------- Délibération (fusion d'ITMCD) ----------

/// <summary>
/// Paramètres de délibération de l'année (projet Délibération, « Paramètres profs ») : encodage par semestre, moyennes
/// d'appui, mise à jour des cotes déjà enregistrées et téléchargements des enseignants, seuil de réussite.
/// </summary>
public sealed record DeliberationSettingsDto(bool EncodingS1Open, bool EncodingS2Open, bool SupportEnabled, decimal PassPercent,
    bool TeacherUpdates = false, bool TeacherDownloads = true);

/// <summary>Code anonyme d'un apprenant inscrit. <c>Code</c> vide : aucun code encore attribué.</summary>
public sealed record AnonymousCodeRowDto(Guid EnrollmentId, string Matricule, string FullName, string PromotionCode, string? Code, bool IsManual, DateTime? AssignedAtUtc);

public sealed record CodeAssignmentRequest(Guid? PromotionId);
public sealed record CodeAssignmentResult(int Assigned, int AlreadyCoded, int Total);

/// <summary>Code officiel saisi à la main, ou tirage d'un nouveau code (<c>Code</c> vide). Remplacer un code exige un motif.</summary>
public sealed record CodeChangeRequest(string? Code, string? Reason);

/// <summary>Résolution d'un code pour l'encodage anonyme : la promotion, jamais le nom.</summary>
public sealed record CodeLookupDto(string Code, Guid EnrollmentId, Guid PromotionId, string PromotionCode);

/// <summary>Cours vu par la délibération : organisation, maxima et avancement de l'encodage.</summary>
public sealed record DeliberationCourseDto(Guid Id, int Number, string Name, string Semester, decimal MaxScore, string? TeacherName,
    int Students, int GradedS1, int GradedS2, int Supports, bool LockedS1, bool LockedS2, bool SubmittedS1 = false, bool SubmittedS2 = false, int GradedJury = 0);

public sealed record MarkRowDto(Guid EnrollmentId, string? Code, string? Matricule, string? FullName, decimal? Value, decimal? Base, decimal? Retained, bool Failed);

/// <summary>Feuille de cotes d'un cours pour une nature (S1, S2 ou Appui). En mode anonyme, noms et matricules sont masqués.
/// <c>SubmittedAtUtc</c> : date de la remise officielle ; après la remise, seul le jury corrige la feuille.</summary>
public sealed record MarkSheetDto(Guid CourseId, string Course, string Semester, decimal MaxScore, string Kind, bool Anonymous, bool Locked, bool EncodingOpen,
    IReadOnlyList<MarkRowDto> Rows, DateTime? SubmittedAtUtc = null, string? SubmittedBy = null, int Failed = 0, decimal? Average = null);

/// <summary>Cote saisie dans la feuille. <c>Value</c> vide efface la cote.</summary>
public sealed record MarkInput(Guid EnrollmentId, decimal? Value);

public sealed record MarkByCodeRequest(string Code, Guid CourseId, string Kind, decimal Value);
public sealed record MarkByCodeResult(string Code, string Course, string Kind, decimal Value, decimal? Previous, int Remaining);

public sealed record CourseOutcomeDto(Guid CourseId, int Number, string Course, string Semester, decimal Max, decimal? Base, decimal Bonus, decimal Points,
    bool Graded, bool Failed);

/// <summary>
/// <c>Grade</c> : code officiel du jury annuel (P, A ou E) ; <c>Totals</c> : TOTAL 1, SOUS-TOTAUX et TOTAL GÉNÉRAL
/// quand la promotion a des épreuves du jury (projet Délibération).
/// </summary>
public sealed record DeliberationRowDto(Guid EnrollmentId, string? Code, string Matricule, string FullName, decimal Points, decimal Max, decimal Percent,
    int Missing, int Failed, string Computed, string Decision, string? JuryOutcome, string? JuryReason, string Mention, int? Rank, bool NearThreshold,
    string? Grade = null, JuryTotalsDto? Totals = null);

public sealed record JuryTotalsDto(decimal Total1, decimal Total1Max, decimal Subtotal1, decimal Subtotal1Max, decimal Subtotal2, decimal Subtotal2Max,
    decimal Total2, decimal Total2Max, decimal General, decimal GeneralMax);

/// <summary>Autorités de l'année (projet Délibération) : elles signent le bulletin, la grille et le procès-verbal.</summary>
public sealed record AuthoritiesDto(string? Prefect, string? Principal, string? JuryPresident, string? JurySecretary, IReadOnlyList<string>? JuryMembers);

public sealed record JuryEvaluationsResult(int Created, int Existing);

public sealed record CourseStatDto(Guid CourseId, int Number, string Course, string Semester, decimal Max, int Passed, int Failed, int Missing, decimal? Average);

/// <summary>Tableau de délibération d'une promotion pour une période : statistiques, cours et apprenants.</summary>
public sealed record DeliberationBoardDto(Guid PromotionId, string Promotion, string Period, string PeriodLabel, string AcademicYear, string Status,
    DateTime? ClosedAtUtc, string? ClosedBy, string? President, string? Members, int ReopenCount, string? LastReopenReason,
    int Students, int Admitted, int Failed, int Incomplete, decimal SuccessRate, decimal PassPercent,
    IReadOnlyList<CourseStatDto> Courses, IReadOnlyList<DeliberationRowDto> Rows);

public sealed record StudentDeliberationDto(DeliberationRowDto Result, string Promotion, string Period, IReadOnlyList<CourseOutcomeDto> Courses);

/// <summary>Décision motivée du jury. <c>Outcome</c> vide retire la décision et rend le résultat calculé.</summary>
public sealed record JuryDecisionRequest(Guid PromotionId, string Period, Guid EnrollmentId, string? Outcome, string? Reason);

public sealed record SessionCloseRequest(Guid PromotionId, string Period, string? Password, string? President, string? Members, string? Notes);
public sealed record SessionReopenRequest(Guid PromotionId, string Period, string? Password, string? Reason);

/// <summary>Renvoi d'une feuille remise à l'enseignant : le motif est obligatoire.</summary>
public sealed record MarkReturnRequest(string? Reason);

public sealed record MarkSubmissionDto(Guid CourseId, string Course, string Kind, DateTime SubmittedAtUtc, string SubmittedBy, int Graded, int Students);

/// <summary>
/// Suivi de l'encodage (moniteur d'ITMCD) : une ligne par cours et par cote attendue (S1 ou S2), avec l'enseignant,
/// l'avancement, la remise et l'état de la délibération.
/// </summary>
public sealed record EncodingProgressRowDto(Guid CourseId, Guid PromotionId, string Promotion, int Number, string Course, string Semester, string Kind,
    string? TeacherName, int Students, int Graded, decimal Percent, int Failed, decimal? Average, DateTime? SubmittedAtUtc, string? SubmittedBy, bool Locked, string Status);

public sealed record EncodingProgressDto(string AcademicYear, string PeriodLabel, int Sheets, int Complete, int Submitted, int Locked, int ExpectedMarks, int GradedMarks,
    decimal Percent, IReadOnlyList<EncodingProgressRowDto> Rows);

/// <summary>Import de codes officiels existants (ITMCD, liste Excel) : <c>Apply</c> faux = aperçu seulement, rien n'est enregistré.</summary>
public sealed record CodeImportRequest(string? Text, Guid? PromotionId, bool Apply);

/// <summary>
/// Ligne de l'aperçu. Statuts : « À attribuer », « Déjà attribué », « Code différent » (l'apprenant a déjà un autre code :
/// remplacement manuel avec motif), « Introuvable », « Ambigu », « Code invalide », « Code pris », « Doublon dans la liste ».
/// </summary>
public sealed record CodeImportLineDto(int Line, string Identifier, string Code, string Status, Guid? EnrollmentId, string? Matricule, string? FullName, string? PromotionCode);

public sealed record CodeImportResult(bool Applied, int Assigned, int Ready, int Unchanged, int Problems, IReadOnlyList<CodeImportLineDto> Lines);

/// <summary>Cours attribué à l'enseignant connecté, avec sa promotion (portail enseignant).</summary>
public sealed record TeacherCourseDto(Guid PromotionId, string Promotion, DeliberationCourseDto Course);

/// <summary>Annonce de la délibération : ouverture ou fermeture de l'encodage, remise ou renvoi d'une feuille.</summary>
public sealed record AnnouncementDto(DateTime AtUtc, string Title, string Text, string By);

/// <summary>Reprise d'une liste de cotes pour un cours (export d'ITMCD, Excel) : <c>Apply</c> faux = aperçu sans écriture.</summary>
public sealed record MarkImportRequest(Guid CourseId, string Kind, string? Text, bool Apply);

/// <summary>
/// Statuts : « À enregistrer », « Remplace », « Inchangée », « Code inconnu », « Hors promotion », « Cote invalide »,
/// « Doublon dans la liste ». Un enseignant identifie les copies par le code anonyme seulement.
/// </summary>
public sealed record MarkImportLineDto(int Line, string Identifier, string Value, string Status, decimal? Previous);

public sealed record MarkImportResult(bool Applied, int Saved, int Ready, int Unchanged, int Problems, IReadOnlyList<MarkImportLineDto> Lines);

/// <summary>Vérification publique d'un bulletin officiel (QR code) : nom masqué, résultat figé à la clôture.</summary>
public sealed record BulletinVerification(bool Found, string? School, string? AcademicYear, string? Promotion, string? Period, string? Student,
    decimal? Percent, string? Decision, string? Grade, DateTime? ClosedAtUtc);
