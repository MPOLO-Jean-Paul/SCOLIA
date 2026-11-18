using System.Globalization;
using System.Text.Json;

namespace JPServices.Owner;

/// <summary>Lecture tolérante des réponses du Worker et libellés français affichés par la console.</summary>
public static class OwnerText
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        } : "";

    public static long? Number(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    /// <summary>D1 renvoie les booléens en 0/1 ; le Worker les renvoie parfois en vrai/faux.</summary>
    public static bool Flag(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n != 0));

    public static JsonElement Child(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static IReadOnlyList<JsonElement> Items(JsonElement? e, string name) =>
        e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().ToList() : [];

    public static string Date(long? ms) => ms is { } v && v > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(v).ToLocalTime().ToString("d MMM yyyy", French) : "—";

    public static string DateTime(long? ms) => ms is { } v && v > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(v).ToLocalTime().ToString("d MMM yyyy 'à' HH:mm", French) : "—";

    /// <summary>« il y a 3 h », pour l'activité des serveurs.</summary>
    public static string Ago(long? ms, DateTimeOffset? now = null)
    {
        if (ms is not { } v || v <= 0) return "jamais";
        var span = (now ?? DateTimeOffset.UtcNow) - DateTimeOffset.FromUnixTimeMilliseconds(v);
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalMinutes < 2 ? "à l'instant"
            : span.TotalHours < 1 ? $"il y a {(int)span.TotalMinutes} min"
            : span.TotalDays < 1 ? $"il y a {(int)span.TotalHours} h"
            : span.TotalDays < 60 ? $"il y a {(int)span.TotalDays} j"
            : "le " + Date(v);
    }

    public static string Bytes(long? bytes) => bytes switch
    {
        null or 0 => "0 o",
        < 1024 => $"{bytes} o",
        < 1024 * 1024 => (bytes.Value / 1024d).ToString("0.#", French) + " Ko",
        < 1024L * 1024 * 1024 => (bytes.Value / 1048576d).ToString("0.#", French) + " Mo",
        _ => (bytes.Value / 1073741824d).ToString("0.##", French) + " Go"
    };

    /// <summary>Statut effectif calculé par le Worker (evaluateLicense), jamais l'ancien champ « state ».</summary>
    public static (string Label, LicenseTone Tone) License(string status) => status switch
    {
        "active" => ("Payée", LicenseTone.Good),
        "trial" => ("Essai", LicenseTone.Info),
        "grace" => ("Délai supplémentaire", LicenseTone.Warning),
        "derogation" => ("Dérogation", LicenseTone.Warning),
        "free" => ("Gratuite", LicenseTone.Info),
        "suspended" => ("Bloquée · lecture seule", LicenseTone.Bad),
        "expired" => ("Échue · lecture seule", LicenseTone.Bad),
        _ => ("Inconnue", LicenseTone.Info)
    };

    public static string LicenseLine(JsonElement license)
    {
        var (label, _) = License(Str(license, "license_status"));
        var days = Number(license, "days_remaining");
        var until = Number(license, "expires_at");
        return days is null ? label
            : days == 0 ? $"{label} · échéance atteinte"
            : $"{label} · {days} j restants (jusqu'au {Date(until)})";
    }

    public static string Action(string action) => action switch
    {
        "invitation_created" => "Invitation créée",
        "recovery_invitation_created" => "Invitation de reprise créée",
        "school_server_recovered" => "Nouveau serveur relié (reprise)",
        "license_policy_changed" => "Licence modifiée",
        "cloud_requested" => "Sauvegarde en ligne proposée",
        "cloud_withdrawn" => "Sauvegarde en ligne retirée",
        "cloud_requested_all" => "Sauvegarde proposée à toutes les écoles",
        "storage_configured" => "Stockage des sauvegardes configuré",
        "storage_removed" => "Stockage des sauvegardes retiré",
        "relay_created" => "Relais ajouté",
        "relay_active" => "Relais activé",
        "relay_disabled" => "Relais désactivé",
        _ => action.Replace('_', ' ')
    };

    /// <summary>Nombre de jours saisi : entier positif, borné.</summary>
    public static int? Days(string? text, int max) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && n <= max ? n : null;
}

public enum LicenseTone { Good, Info, Warning, Bad }
