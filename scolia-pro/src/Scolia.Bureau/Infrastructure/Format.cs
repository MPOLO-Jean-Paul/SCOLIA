using System.Globalization;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Mise en forme française des montants, dates et libellés.</summary>
public static class Format
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static string Money(decimal value, string symbol) => $"{value.ToString("N0", Fr)} {symbol}";

    public static string Amount(decimal value) => value.ToString("N0", Fr);

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", Fr);
    /// <summary>Date en toutes lettres, avec la majuscule du jour : « Samedi 25 octobre 2026 ».</summary>
    public static string LongDate(DateOnly date) { var t = date.ToString("dddd d MMMM yyyy", Fr); return char.ToUpper(t[0], Fr) + t[1..]; }

    public static string DateTime(DateTime utc) => utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Fr);

    public static string Percent(decimal value) => $"{value.ToString("0.0", Fr)} %";

    public static string Count(int value, string singular, string plural) => $"{value.ToString("N0", Fr)} {(value > 1 ? plural : singular)}";

    public static string FullName(string last, string middle, string first) =>
        string.Join(' ', new[] { last, middle, first }.Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>Lit un montant saisi à la française : espaces et virgule décimale acceptés.</summary>
    public static bool TryAmount(string? text, out decimal value)
    {
        // Toutes les espaces, y compris insécables et fines, sont des séparateurs de milliers.
        var digits = new string((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace(',', '.');
        return decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Lit une date saisie au format jour/mois/année ou année-mois-jour.</summary>
    public static bool TryDay(string? text, out DateOnly day)
    {
        var formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };
        return DateOnly.TryParseExact((text ?? string.Empty).Trim(), formats, Fr, DateTimeStyles.None, out day);
    }
}

/// <summary>Libellés affichés pour les codes échangés avec le serveur.</summary>
public static class Labels
{
    public static string Status(string code) => code switch
    {
        "Actif" => "Actif",
        "Suspendu" => "Suspendu",
        "Sorti" => "Sorti",
        "Diplome" => "Diplômé",
        "Transfere" => "Transféré",
        "Annule" => "Annulé",
        "Valide" => "Validé",
        "Brouillon" => "Brouillon",
        "Paye" => "Payé",
        "Impaye" => "Impayé",
        "Partiel" => "Partiel",
        "Couvert" => "Couvert",
        "Preparation" => "Préparation",
        "Open" => "Ouverte",
        "Closed" => "Clôturée",
        _ => code,
    };

    public static string Method(string code) => code switch
    {
        "Especes" => "Espèces",
        "MobileMoney" => "Mobile Money",
        "Banque" => "Banque",
        "Cheque" => "Chèque",
        "Autre" => "Autre",
        _ => code,
    };

    public static string Gender(string code) => code == "F" ? "Féminin" : "Masculin";

    public static string Severity(string code) => code switch
    {
        "Mineur" => "Mineure",
        "Moyen" => "Moyenne",
        _ => code,
    };

    public static string Kind(string code) => code switch
    {
        "Interrogation" => "Interrogation",
        "Devoir" => "Devoir",
        "TravauxPratiques" => "Travaux pratiques",
        "Examen" => "Examen",
        _ => code,
    };

    /// <summary>Tonalité d'un statut : vert pour le sain, orange pour l'attention, rouge pour l'échec.</summary>
    public static string Tone(string code) => code switch
    {
        "Valide" or "Actif" or "Paye" or "Couvert" or "Open" => "green",
        "Annule" or "Sorti" or "Grave" or "Impaye" => "red",
        "Suspendu" or "Partiel" or "Moyen" or "Retard" or "Brouillon" => "orange",
        _ => "neutral",
    };

    public static readonly IReadOnlyList<ChoiceItem> Methods =
    [
        new("Especes", "Espèces"), new("MobileMoney", "Mobile Money"), new("Banque", "Banque"), new("Cheque", "Chèque"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Genders = [new("M", "Masculin"), new("F", "Féminin")];

    public static readonly IReadOnlyList<ChoiceItem> Statuses =
    [
        new("Actif", "Actif"), new("Suspendu", "Suspendu"), new("Sorti", "Sorti"), new("Diplome", "Diplômé"), new("Transfere", "Transféré"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Attendance =
    [
        new("Présent", "Présent"), new("Retard", "Retard"), new("Absent", "Absent"), new("Absence justifiée", "Justifié"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Severities =
    [
        new("Mineur", "Mineure"), new("Moyen", "Moyenne"), new("Grave", "Grave"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> AssessmentKinds =
    [
        new("Interrogation", "Interrogation"), new("Devoir", "Devoir"), new("TravauxPratiques", "Travaux pratiques"), new("Examen", "Examen"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Roles =
    [
        // Deux comptes, comme la V79 : le secrétariat administre SCOLIA, la comptabilité tient la caisse.
        new("Secretariat", "Secrétariat"), new("Comptabilite", "Comptabilité"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> PayrollModes =
    [
        new("Especes", "Espèces"), new("Banque", "Banque"), new("MobileMoney", "Mobile Money"), new("Cheque", "Chèque"),
    ];

    public static readonly IReadOnlyList<ChoiceItem> Buckets =
    [
        new("", "Toutes"), new("0-30 jours", "0-30 jours"), new("31-60 jours", "31-60 jours"), new("61-90 jours", "61-90 jours"), new("Plus de 90 jours", "Plus de 90 jours"),
    ];
}
