using System.Text.RegularExpressions;
using Scolia.Domain.Common;

namespace Scolia.Domain.Students;

public enum Gender { M = 0, F = 1 }

/// <summary>Dossier permanent d'un apprenant (indépendant de l'année scolaire).</summary>
public sealed class Student : Entity
{
    public string Matricule { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public Gender Gender { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? BirthPlace { get; set; }
    public string? Nationality { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GuardianName { get; set; }
    public string? GuardianRelation { get; set; }
    public string? GuardianPhone { get; set; }
    public string? GuardianPhone2 { get; set; }
    public string? Notes { get; set; }
    public byte[]? Photo { get; set; }
    /// <summary>Clé normalisée nom|post-nom|prénom pour la détection des doublons.</summary>
    public string IdentityKey { get; set; } = string.Empty;
    /// <summary>Texte normalisé pour la recherche rapide.</summary>
    public string SearchText { get; set; } = string.Empty;
    public List<Enrollment> Enrollments { get; set; } = [];

    /// <summary>
    /// Numéro pour joindre la famille, saisi à l'inscription : le contact parent 1, sinon le contact parent 2,
    /// sinon le téléphone de l'apprenant. Les relances WhatsApp et SMS partent toutes de ce numéro.
    /// </summary>
    public string? ParentPhone => new[] { GuardianPhone, GuardianPhone2, Phone }.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

    public string FullName => string.Join(' ', new[] { LastName, MiddleName, FirstName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public void RefreshKeys()
    {
        IdentityKey = BuildIdentityKey(LastName, MiddleName, FirstName);
        SearchText = TextKey.Normalize($"{Matricule} {LastName} {MiddleName} {FirstName} {GuardianName} {GuardianPhone} {GuardianPhone2} {Phone}");
    }

    public static string BuildIdentityKey(string? last, string? middle, string? first) =>
        $"{TextKey.Normalize(last)}|{TextKey.Normalize(middle)}|{TextKey.Normalize(first)}";
}

public enum EnrollmentStatus { Actif = 0, Suspendu = 1, Sorti = 2, Diplome = 3, Transfere = 4 }

/// <summary>Inscription d'un apprenant dans une promotion pour une année scolaire.</summary>
public sealed class Enrollment : Entity
{
    public Guid StudentId { get; set; }
    public Student? Student { get; set; }
    public Guid AcademicYearId { get; set; }
    public Guid PromotionId { get; set; }
    public DateOnly EnrolledOn { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Actif;
    public DateOnly? ExitDate { get; set; }
    public string? ExitReason { get; set; }
    /// <summary>Indique une réinscription (l'apprenant était inscrit l'année précédente).</summary>
    public bool IsReEnrollment { get; set; }
}

/// <summary>Prise en charge (bourse, sponsor, exonération) d'une partie des frais d'une inscription.</summary>
public sealed class Sponsorship : Entity
{
    public Guid EnrollmentId { get; set; }
    public string SponsorName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
    public bool IsActive { get; set; } = true;
    public List<SponsorshipLine> Lines { get; set; } = [];
}

/// <summary>Part couverte d'une rubrique de frais, en pourcentage (100 = totalement couverte).</summary>
public sealed class SponsorshipLine : Entity
{
    public Guid SponsorshipId { get; set; }
    public Guid FeeItemId { get; set; }
    public decimal Percent { get; set; }
}

/// <summary>Règles d'identité reprises de SCOLIA V79 et durcies.</summary>
public static partial class IdentityRules
{
    [GeneratedRegex(@"^[\p{L} '’\-]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^0\d{9}$")]
    private static partial Regex LocalPhone();

    [GeneratedRegex(@"^\+?243(\d{9})$")]
    private static partial Regex CongoInternational();

    /// <summary>Longueur d'un numéro de téléphone : 10 chiffres, comme 0812345678.</summary>
    public const int PhoneLength = 10;

    public static bool IsValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 80 && NamePattern().IsMatch(value.Trim());

    /// <summary>Un numéro de téléphone compte 10 chiffres et rien d'autre (0812345678). Les espaces, points et tirets sont retirés ;
    /// un ancien numéro saisi au format +243 est ramené à 10 chiffres.</summary>
    public static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = new string(value.Where(c => !char.IsWhiteSpace(c) && c is not ('.' or '-' or '/' or '(' or ')')).ToArray());
        if (CongoInternational().Match(compact) is { Success: true } international) compact = "0" + international.Groups[1].Value;
        if (LocalPhone().IsMatch(compact)) return compact;
        throw new DomainException("telephone", $"Numéro de téléphone invalide : {value}. Saisissez 10 chiffres, par exemple 0812345678.");
    }

    /// <summary>Numéro au format international sans « + » pour les liens WhatsApp.</summary>
    public static string? ToInternational(string? phone, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (phone.TrimStart().StartsWith('+')) return digits;
        if (digits.Length == 10 && digits[0] == '0') return countryCode + digits[1..];
        return digits;
    }
}

/// <summary>Génération des matricules : code de promotion (4 caractères) + rang à 2 caractères.
/// Format identique à SCOLIA V79 (INF101…INF199 puis INF1A0…).</summary>
public static class MatriculeGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static string Next(string promotionCode, IEnumerable<string> existing)
    {
        var promo = School.Promotion.NormalizeCode(promotionCode);
        if (promo.Length != 4 || !promo.All(char.IsAsciiLetterOrDigit))
            throw new DomainException("matricule", "Le code de la promotion doit compter 4 lettres ou chiffres (ex. INF1).");
        var taken = new HashSet<string>(existing.Select(TextKey.Normalize), StringComparer.Ordinal);
        for (var n = 1; n <= 99; n++)
        {
            var code = promo + n.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            if (!taken.Contains(code)) return code;
        }
        foreach (var a in Alphabet)
        foreach (var b in Alphabet)
        {
            if (char.IsDigit(a) && char.IsDigit(b)) continue;
            var code = $"{promo}{a}{b}";
            if (!taken.Contains(code)) return code;
        }
        throw new DomainException("matricule", "Les matricules disponibles pour cette promotion sont épuisés.");
    }

    public static bool IsValid(string? matricule)
    {
        var m = TextKey.Normalize(matricule);
        return m.Length is >= 4 and <= 20 && m.All(char.IsAsciiLetterOrDigit);
    }
}
