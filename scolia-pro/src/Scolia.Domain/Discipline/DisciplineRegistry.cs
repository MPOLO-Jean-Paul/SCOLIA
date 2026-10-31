using Scolia.Domain.Common;
using Scolia.Domain.School;
using Scolia.Domain.Students;

namespace Scolia.Domain.Discipline;

/// <summary>Mesure inscrite au registre disciplinaire. Une convocation n'est jamais un renvoi.</summary>
public enum DisciplineMeasure { RenvoiTemporaire = 0, RenvoiDefinitif = 1, Convocation = 2 }

/// <summary>Suite donnée à une convocation : parent reçu, ou absent après l'heure du rendez-vous.</summary>
public enum ParentReception { EnAttente = 0, Recu = 1, Absent = 2 }

public enum DecisionState { Validee = 0, Annulee = 1 }

/// <summary>
/// Décision du registre disciplinaire. Une décision n'est jamais effacée ni réécrite : pour corriger,
/// on l'annule puis on enregistre une nouvelle décision.
/// </summary>
public sealed class DisciplineDecision : Entity
{
    public Guid EnrollmentId { get; set; }
    public Guid AcademicYearId { get; set; }
    public DisciplineMeasure Measure { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateOnly DecisionDate { get; set; }
    /// <summary>Premier et dernier jours d'un renvoi temporaire, bornes incluses.</summary>
    public DateOnly? FirstDay { get; set; }
    public DateOnly? LastDay { get; set; }
    /// <summary>Rendez-vous d'une convocation, date et heure locales.</summary>
    public DateTime? AppointmentAt { get; set; }
    /// <summary>Vrai pour un renvoi définitif créé par la règle des deux renvois temporaires.</summary>
    public bool IsAutomatic { get; set; }
    /// <summary>Parent connu au moment de la décision : ces coordonnées restent figées.</summary>
    public string? ParentName { get; set; }
    public string? ParentPhone { get; set; }
    public ParentReception Reception { get; set; }
    public string? ReceptionNote { get; set; }
    public DateTime? ReceptionAtUtc { get; set; }
    public DecisionState State { get; set; } = DecisionState.Validee;
    public string? CancelReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelledByName { get; set; }
    public string RecordedByName { get; set; } = string.Empty;
    /// <summary>Clé de répétition : un même enregistrement répété ne crée pas une seconde sanction.</summary>
    public string? RequestKey { get; set; }

    /// <summary>Date à partir de laquelle la décision prend effet dans le registre.</summary>
    public DateOnly EffectiveFrom => FirstDay ?? DecisionDate;
}

/// <summary>Règles de la discipline des apprenants, calculées sans accès à la base.</summary>
public static class DisciplineRules
{
    public const int MinReasonLength = 5;
    public const int MaxReasonLength = 2000;

    public static string Reason(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length is < MinReasonLength or > MaxReasonLength)
            throw new DomainException("discipline", $"Le motif doit compter entre {MinReasonLength} et {MaxReasonLength} caractères.");
        return trimmed;
    }

    /// <summary>Durée d'un renvoi en jours calendaires, premier et dernier jours inclus.</summary>
    public static int Days(DateOnly first, DateOnly last) => last.DayNumber - first.DayNumber + 1;

    /// <summary>Le retour est autorisé à partir du lendemain de la fin du renvoi.</summary>
    public static DateOnly ReturnFrom(DateOnly lastDay) => lastDay.AddDays(1);

    /// <summary>Un renvoi validé, temporaire ou définitif. Une convocation et une décision annulée n'en sont pas.</summary>
    public static bool IsExclusion(DisciplineDecision decision) =>
        decision.State == DecisionState.Validee && decision.Measure != DisciplineMeasure.Convocation;

    public static bool IsDefinitive(DisciplineDecision decision) =>
        IsExclusion(decision) && decision.Measure == DisciplineMeasure.RenvoiDefinitif;

    public static bool IsTemporaryOn(DisciplineDecision decision, DateOnly day) =>
        decision.State == DecisionState.Validee
        && decision.Measure == DisciplineMeasure.RenvoiTemporaire
        && decision.FirstDay is { } first && decision.LastDay is { } last
        && first <= day && day <= last;

    /// <summary>
    /// Statut affiché : un renvoi définitif donne Sorti, un renvoi temporaire en cours donne Suspendu.
    /// Sinon le statut administratif initial reprend ses droits.
    /// </summary>
    public static EnrollmentStatus EffectiveStatus(EnrollmentStatus baseStatus, IEnumerable<DisciplineDecision> decisions, DateOnly today)
    {
        var list = decisions.ToList();
        if (list.Any(IsDefinitive)) return EnrollmentStatus.Sorti;
        if (list.Any(d => IsTemporaryOn(d, today))) return EnrollmentStatus.Suspendu;
        return baseStatus;
    }

    /// <summary>
    /// Règle des deux renvois : le second renvoi temporaire validé dans l'année devient un renvoi définitif.
    /// </summary>
    public static bool BecomesDefinitive(DisciplineMeasure requested, int validTemporaryThisYear) =>
        requested == DisciplineMeasure.RenvoiTemporaire && validTemporaryThisYear >= 1;

    /// <summary>
    /// Contrôles d'un renvoi : dates dans l'année scolaire, fin après début, décision non future,
    /// et pas de chevauchement avec un renvoi temporaire déjà validé.
    /// </summary>
    public static void ValidateExclusion(DateOnly decisionDate, DateOnly first, DateOnly last, AcademicYear year, DateOnly today,
        IEnumerable<DisciplineDecision> existing)
    {
        ValidateDecisionDate(decisionDate, today);
        if (last < first) throw new DomainException("discipline", "La fin du renvoi doit suivre ou égaler son début.");
        if (first < year.StartDate || last > year.EndDate)
            throw new DomainException("discipline", "Les dates du renvoi doivent appartenir à l'année scolaire.");
        foreach (var other in existing.Where(IsExclusion))
        {
            if (other.Measure == DisciplineMeasure.RenvoiTemporaire && other.FirstDay is { } f && other.LastDay is { } l && first <= l && last >= f)
                throw new DomainException("discipline", "Ce renvoi chevauche un renvoi temporaire déjà enregistré.");
            if (other.EffectiveFrom > first)
                throw new DomainException("discipline", "Un renvoi plus récent existe déjà : ce renvoi, saisi avant lui, est refusé pour garder la chronologie.");
        }
    }

    public static void ValidateDecisionDate(DateOnly decisionDate, DateOnly today)
    {
        if (decisionDate > today) throw new DomainException("discipline", "Les décisions futures ne sont pas enregistrées.");
    }

    /// <summary>Une convocation garde un rendez-vous ; l'absence du parent ne se constate qu'après l'heure prévue.</summary>
    public static void ValidateReception(DisciplineDecision convocation, ParentReception reception, string? note, DateTime nowLocal)
    {
        if (convocation.Measure != DisciplineMeasure.Convocation)
            throw new DomainException("discipline", "Seule une convocation peut recevoir une suite (parent reçu ou absent).");
        if (convocation.State != DecisionState.Validee)
            throw new DomainException("discipline", "Cette convocation est annulée.");
        if (reception == ParentReception.EnAttente)
            throw new DomainException("discipline", "Indiquez si le parent a été reçu ou s'il est absent.");
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("discipline", "Un compte rendu accompagne la réception ou l'absence du parent.");
        if (reception == ParentReception.Absent && convocation.AppointmentAt is { } appointment && nowLocal < appointment)
            throw new DomainException("discipline", "Le parent ne peut être déclaré absent qu'après l'heure du rendez-vous.");
    }
}
