using Material.Icons;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Iconographie sémantique unique des familles d'outils. Les mêmes repères sont
/// utilisés sur ordinateur, téléphone et tablette ; aucune icône générique répétée.
/// Les couleurs sont des ressources du thème, pas des couleurs codées dans les vues.
/// </summary>
public static class FeatureVisuals
{
    public static MaterialIconKind Icon(string title) => title switch
    {
        "" or "Accueil" => MaterialIconKind.Home,
        "Données" or "Sauvegardes" => MaterialIconKind.DatabaseExport,
        "Contrôle" => MaterialIconKind.DatabaseCheck,
        "Effacement" => MaterialIconKind.DeleteSweep,
        "Secrétariat" => MaterialIconKind.AccountGroup,
        "Finances" or "Paiements" => MaterialIconKind.CashMultiple,
        "Scolarité" => MaterialIconKind.School,
        "Administration" => MaterialIconKind.Briefcase,
        "Paramètres" or "Préférences" => MaterialIconKind.Cog,
        "Sécurité" => MaterialIconKind.ShieldCheck,
        "Exports" or "Export" or "Rapports" => MaterialIconKind.FileChart,
        "Aide" => MaterialIconKind.InformationOutline,
        _ => MaterialIconKind.ViewDashboard
    };

    /// <summary>Couleur fonctionnelle des petites commandes, jamais une teinte arbitraire.</summary>
    public static string CommandTone(string label, string originalTone)
    {
        if (originalTone != "ToneNeutral") return originalTone;
        if (label.StartsWith("Effacer", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Annuler", StringComparison.OrdinalIgnoreCase)) return "ToneRed";
        if (label.StartsWith("Nouvel", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Sauvegarder", StringComparison.OrdinalIgnoreCase)) return "ToneGreen";
        if (label.Contains("reçu", StringComparison.OrdinalIgnoreCase)
            || label.Contains("débiteur", StringComparison.OrdinalIgnoreCase)) return "ToneOrange";
        if (label.Contains("PDF", StringComparison.OrdinalIgnoreCase)) return "ToneIndigo";
        if (label.Contains("Excel", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Importer", StringComparison.OrdinalIgnoreCase)) return "ToneTeal";
        if (label.StartsWith("Vérifier", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Sécurité", StringComparison.OrdinalIgnoreCase)) return "ToneTeal";
        if (label.Contains("Police", StringComparison.OrdinalIgnoreCase)
            || label.Contains("texte", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Préférences", StringComparison.OrdinalIgnoreCase)) return "TonePurple";
        return "ToneBlue";
    }

    public static string Tone(string title) => title switch
    {
        "" or "Accueil" or "Secrétariat" => "ToneBlue",
        "Données" or "Exports" or "Export" or "Rapports" => "ToneIndigo",
        "Sauvegardes" or "Contrôle" => "ToneTeal",
        "Effacement" => "ToneRed",
        "Finances" or "Paiements" => "ToneGreen",
        "Scolarité" => "TonePurple",
        "Administration" => "ToneOrange",
        "Paramètres" or "Préférences" => "ToneTeal",
        "Sécurité" => "ToneRed",
        "Aide" => "ToneBlue",
        _ => "ToneIndigo"
    };
}
