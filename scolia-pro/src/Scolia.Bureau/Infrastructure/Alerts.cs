using Material.Icons;
using Scolia.Bureau.ViewModels;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Ce qui mérite une bannière : un encaissement ou une sauvegarde faits sur un autre poste.</summary>
public enum AlertKind { Payment, PaymentCancelled, PaymentBatch, Backup, DailyReport, Deliberation }

/// <summary>Une alerte prête à montrer : bannière en haut de l'écran, ou notification du téléphone en arrière-plan.</summary>
public sealed record AppAlert(AlertKind Kind, string Title, string Text, string Origin, string? Data = null)
{
    /// <summary>Même étiquette pour une même sorte d'alerte : Android regroupe au lieu d'empiler.</summary>
    public string Tag => Kind switch
    {
        AlertKind.Backup => "scolia-sauvegarde",
        AlertKind.DailyReport => "scolia-rapport",
        AlertKind.Deliberation => "scolia-deliberation",
        _ => "scolia-encaissement",
    };

    public MaterialIconKind Icon => Kind switch
    {
        AlertKind.Backup => MaterialIconKind.DatabaseCheckOutline,
        AlertKind.DailyReport => MaterialIconKind.FileChartOutline,
        AlertKind.PaymentCancelled => MaterialIconKind.ReceiptTextRemoveOutline,
        AlertKind.Deliberation => MaterialIconKind.Gavel,
        _ => MaterialIconKind.CashCheck,
    };

    public string Tone => Kind switch
    {
        AlertKind.Backup => "ToneBlue",
        AlertKind.DailyReport => "ToneIndigo",
        AlertKind.PaymentCancelled => "ToneOrange",
        AlertKind.Deliberation => "ToneIndigo",
        _ => "ToneGreen",
    };
}

public static class Alerts
{
    /// <summary>
    /// Lit un changement venu d'un autre poste. Seuls les encaissements (et leurs annulations) et les sauvegardes
    /// deviennent une alerte ; le reste se met à jour sans déranger.
    /// </summary>
    public static AppAlert? From(LiveEventDto change, Preferences prefs, Func<string, bool>? can = null)
    {
        if (!prefs.Alerts) return null;
        var summary = (change.Summary ?? string.Empty).Trim();
        var origin = string.Join(" · ", new[] { change.User, change.Device }.Where(s => !string.IsNullOrWhiteSpace(s)));
        // Notifications de la délibération (ITMCD) : les enseignants apprennent l'ouverture ou la fermeture de l'encodage
        // et le renvoi d'une feuille ; le jury apprend chaque remise de cotes.
        if (can is not null && summary.Length > 0)
        {
            var teacher = can(Ribbon.Perm.MarksEncode) || can(Ribbon.Perm.JuryEncode);
            if (change.Topics.Contains("ReglagesDeliberation") && (teacher || can(Ribbon.Perm.DeliberationManage)))
                return new AppAlert(AlertKind.Deliberation, "Encodage des cotes", summary, origin);
            if (change.Topics.Contains("RemiseCotes"))
            {
                var returned = summary.Contains("renvoyée", StringComparison.OrdinalIgnoreCase);
                if (returned && teacher) return new AppAlert(AlertKind.Deliberation, "Feuille de cotes renvoyée", summary, origin);
                if (!returned && can(Ribbon.Perm.DeliberationManage)) return new AppAlert(AlertKind.Deliberation, "Cotes remises", summary, origin);
            }
        }
        if (change.Topics.Contains("RapportJournalier"))
            return prefs.AlertReports
                ? new AppAlert(AlertKind.DailyReport, "Rapport du jour prêt", summary, "Touchez pour ouvrir le PDF",
                    change.Topics.FirstOrDefault(t => t.Length == 10 && t[4] == '-'))
                : null;
        if (change.Topics.Contains("Sauvegarde"))
            return prefs.AlertBackups ? new AppAlert(AlertKind.Backup, "Nouvelle sauvegarde", summary, origin) : null;
        if (!change.Topics.Contains("Recu") || !prefs.AlertPayments) return null;
        if (summary.Contains("annulé", StringComparison.OrdinalIgnoreCase))
            return new AppAlert(AlertKind.PaymentCancelled, "Reçu annulé", summary, origin);
        if (summary.StartsWith("Lot de", StringComparison.Ordinal))
            return new AppAlert(AlertKind.PaymentBatch, "Encaissement par lot", summary, origin);
        return summary.StartsWith("Reçu", StringComparison.Ordinal)
            ? new AppAlert(AlertKind.Payment, "Nouvel encaissement", summary, origin)
            : null;
    }
}
