using Material.Icons;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Iconographie sémantique unique des familles d'outils. Les mêmes repères sont
/// utilisés sur ordinateur, téléphone et tablette ; aucune icône générique répétée.
/// Les couleurs sont des ressources du thème, pas des couleurs codées dans les vues.
/// </summary>
public static partial class FeatureVisuals
{
    public static MaterialIconKind Icon(string title) => title switch
    {
        "" or "Accueil" => MaterialIconKind.Home,
        "Données" => MaterialIconKind.DatabaseExport,
        "Sauvegardes" => MaterialIconKind.CloudLock,
        "Contrôle" => MaterialIconKind.DatabaseCheck,
        "Effacement" => MaterialIconKind.DeleteSweep,
        "Secrétariat" => MaterialIconKind.AccountGroup,
        "Organisation pédagogique" => MaterialIconKind.SchoolOutline,
        "Réseau et postes" => MaterialIconKind.LanConnect,
        "Finances" or "Paiements" => MaterialIconKind.CashMultiple,
        "Scolarité" => MaterialIconKind.School,
        "Délibération" => MaterialIconKind.Gavel,
        "Espace enseignant" or "Mon espace" => MaterialIconKind.AccountSchool,
        "Enseignants et cours" => MaterialIconKind.HumanMaleBoard,
        "Délibérations" => MaterialIconKind.Gavel,
        "Administration" or "Personnel" => MaterialIconKind.Briefcase,
        "Pilotage" => ForPage<PromoterDashboardPage>(),
        "Paramètres" or "Préférences" => MaterialIconKind.Cog,
        "Sécurité" => MaterialIconKind.ShieldCheck,
        "Exports" or "Export" or "Rapports" => MaterialIconKind.FileChart,
        "Aide" => MaterialIconKind.InformationOutline,
        _ => MaterialIconKind.ViewDashboard
    };

    /// <summary>Couleur fonctionnelle des petites commandes, jamais une teinte arbitraire.</summary>
    public static string CommandTone(string label, string originalTone)
    {
        if (label.StartsWith("Effacer", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Annuler", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Supprimer", StringComparison.OrdinalIgnoreCase)) return "ToneRed";
        if (originalTone != "ToneNeutral") return NavigationTone(originalTone);
        if (label.StartsWith("Nouvel", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Sauvegarder", StringComparison.OrdinalIgnoreCase)) return "ToneGreen";
        if (label.Contains("reçu", StringComparison.OrdinalIgnoreCase)
            || label.Contains("débiteur", StringComparison.OrdinalIgnoreCase)) return "ToneGreen";
        if (label.Contains("PDF", StringComparison.OrdinalIgnoreCase)) return "ToneBlue";
        if (label.Contains("Excel", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Importer", StringComparison.OrdinalIgnoreCase)) return "ToneGreen";
        if (label.StartsWith("Vérifier", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Sécurité", StringComparison.OrdinalIgnoreCase)) return "ToneGreen";
        if (label.Contains("Police", StringComparison.OrdinalIgnoreCase)
            || label.Contains("texte", StringComparison.OrdinalIgnoreCase)
            || label.Contains("Préférences", StringComparison.OrdinalIgnoreCase)) return "ToneBlue";
        return "ToneBlue";
    }

    public static string Tone(string title) => title switch
    {
        "Finances" or "Paiements" or "Sauvegardes" or "Contrôle" or "Réseau et postes" or "Sécurité" => "ToneGreen",
        _ => "ToneBlue"
    };
}
