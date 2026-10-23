using Scolia.Domain.Common;

namespace Scolia.Domain.School;

/// <summary>Établissement (une seule instance par base).</summary>
public sealed class SchoolProfile : Entity
{
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string? Motto { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; } = "République démocratique du Congo";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    /// <summary>Code ISO 4217 de la monnaie (CDF par défaut).</summary>
    public string CurrencyCode { get; set; } = "CDF";
    public string CurrencySymbol { get; set; } = "FC";
    /// <summary>Les montants sont exprimés en unités entières (pas de centimes).</summary>
    public bool WholeAmountsOnly { get; set; } = true;
    public string? ReceiptFooter { get; set; }
    public byte[]? Logo { get; set; }
    public string? LogoContentType { get; set; }
    /// <summary>Indicatif téléphonique international utilisé pour les rappels WhatsApp/SMS.</summary>
    public string PhoneCountryCode { get; set; } = "243";
}

public enum AcademicYearStatus { Preparation = 0, Open = 1, Closed = 2 }

/// <summary>Année scolaire (ex. 2026-2027) et ses périodes de facturation.</summary>
public sealed class AcademicYear : Entity
{
    public string Label { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public AcademicYearStatus Status { get; set; } = AcademicYearStatus.Open;
    public bool IsCurrent { get; set; }
    public List<AcademicPeriod> Periods { get; set; } = [];

    public static string NormalizeLabel(string? value)
    {
        var text = (value ?? string.Empty).Trim().Replace('–', '-').Replace('—', '-').Replace(" ", string.Empty, StringComparison.Ordinal);
        if (text.Length != 9 || text[4] != '-' || !int.TryParse(text[..4], out var first) || !int.TryParse(text[5..], out var second) || second != first + 1 || first < 2000 || first > 2199)
            throw new DomainException("annee", "L'année scolaire doit avoir la forme 2026-2027.");
        return text;
    }

    public int FirstCalendarYear => int.Parse(Label[..4], System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Période de facturation (mois scolaire).</summary>
public sealed class AcademicPeriod : Entity
{
    public Guid AcademicYearId { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Month { get; set; }
    public int CalendarYear { get; set; }
    /// <summary>Date à partir de laquelle la mensualité est exigible.</summary>
    public DateOnly DueDate { get; set; }

    public static readonly string[] FrenchMonths =
        ["Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"];

    /// <summary>Construit les périodes mensuelles d'une année scolaire.
    /// La mensualité d'un mois est exigible le <paramref name="dueDay"/> du mois suivant (règle V79 : le 6).</summary>
    public static List<AcademicPeriod> Monthly(Guid yearId, int firstCalendarYear, int startMonth, int count, int dueDay = 6)
    {
        if (startMonth is < 1 or > 12) throw new DomainException("periodes", "Mois de rentrée invalide.");
        if (count is < 1 or > 12) throw new DomainException("periodes", "Le nombre de mois facturés doit être compris entre 1 et 12.");
        if (dueDay is < 1 or > 28) throw new DomainException("periodes", "Le jour d'échéance doit être compris entre 1 et 28.");
        var list = new List<AcademicPeriod>(count);
        for (var i = 0; i < count; i++)
        {
            var month = startMonth + i;
            var year = firstCalendarYear;
            while (month > 12) { month -= 12; year++; }
            var due = new DateOnly(year, month, dueDay).AddMonths(1);
            list.Add(new AcademicPeriod
            {
                AcademicYearId = yearId,
                Order = i + 1,
                Name = FrenchMonths[month - 1],
                Month = month,
                CalendarYear = year,
                DueDate = due,
            });
        }
        return list;
    }
}

/// <summary>Section ou option (ex. Sciences infirmières).</summary>
public sealed class Section : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>Promotion (classe) pour une année scolaire.</summary>
public sealed class Promotion : Entity
{
    public Guid AcademicYearId { get; set; }
    public Guid? SectionId { get; set; }
    public Section? Section { get; set; }
    /// <summary>Code court utilisé dans les matricules (4 caractères, ex. INF1).</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public int? Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Promotion suivante pour les réinscriptions (ex. INF1 → INF2).</summary>
    public string? NextPromotionCode { get; set; }
    public List<FeeItem> Fees { get; set; } = [];

    public static string NormalizeCode(string? value)
    {
        var code = TextKey.Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (code == "TML1") code = "TLM1"; // Correction historique SCOLIA V79.
        return code;
    }
}

public enum FeeKind { Mensualite = 0, Inscription = 1, Examen = 2, Autre = 3 }

/// <summary>Rubrique de frais exigible pour une promotion (mensualité, inscription, examen…).</summary>
public sealed class FeeItem : Entity
{
    public Guid PromotionId { get; set; }
    public Guid? PeriodId { get; set; }
    public FeeKind Kind { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public int Order { get; set; }
}
