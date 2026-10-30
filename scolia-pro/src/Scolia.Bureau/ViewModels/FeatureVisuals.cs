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
        "Données" => MaterialIconKind.DatabaseExport,
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

    public static string Tone(string title) => title switch
    {
        "" or "Accueil" or "Secrétariat" => "ToneBlue",
        "Données" or "Exports" or "Export" or "Rapports" => "ToneIndigo",
        "Finances" or "Paiements" => "ToneGreen",
        "Scolarité" => "TonePurple",
        "Administration" => "ToneOrange",
        "Paramètres" or "Préférences" => "ToneTeal",
        "Sécurité" => "ToneRed",
        "Aide" => "ToneBlue",
        _ => "ToneIndigo"
    };
}
