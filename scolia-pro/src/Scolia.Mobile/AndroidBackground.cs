using Android.Content;
using Android.OS;
using Android.Provider;
using Scolia.Bureau.Infrastructure;
using AndroidUri = Android.Net.Uri;

namespace Scolia.Mobile;

/// <summary>
/// Téléphone serveur : SCOLIA doit continuer à répondre aux autres postes écran éteint, application réduite.
/// Le service au premier plan garde le processeur et le Wi-Fi éveillés ; il faut en plus qu'Android ne le freine
/// pas pour économiser la batterie. Certaines marques ajoutent leur propre gestionnaire : SCOLIA indique où le régler.
/// </summary>
public static class AndroidBackground
{
    private static bool askedThisLaunch;

    /// <summary>Vrai quand Android laisse SCOLIA hors des économies de batterie.</summary>
    public static bool IsAllowed(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23)) return true;
        return context.GetSystemService(Context.PowerService) is PowerManager power
            && power.IsIgnoringBatteryOptimizations(context.PackageName);
    }

    /// <summary>Fenêtre d'Android : « Autoriser SCOLIA à toujours s'exécuter en arrière-plan ? ».</summary>
    public static void Ask(Context context)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23) || IsAllowed(context)) return;
        try
        {
            var intent = new Intent(Settings.ActionRequestIgnoreBatteryOptimizations, AndroidUri.Parse($"package:{context.PackageName}"));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (ActivityNotFoundException)
        {
            // Quelques surcouches retirent cette fenêtre : la liste des économies de batterie reste accessible.
            OpenBatteryList(context);
        }
    }

    /// <summary>Une seule demande par lancement, et seulement sur le téléphone serveur.</summary>
    public static void AskOnceIfServer(Context context)
    {
        if (ConnectionStore.Load()?.Mode != ConnectionSettings.Local) return;
        AskOnce(context);
    }

    /// <summary>Le téléphone devient serveur : la demande vient tout de suite, une fois par lancement.</summary>
    public static void AskOnce(Context context)
    {
        if (askedThisLaunch || IsAllowed(context)) return;
        askedThisLaunch = true;
        Ask(context);
    }

    /// <summary>Page de SCOLIA dans les réglages : batterie, démarrage automatique, notifications.</summary>
    public static void OpenSettings(Context context)
    {
        try
        {
            var intent = new Intent(Settings.ActionApplicationDetailsSettings, AndroidUri.Parse($"package:{context.PackageName}"));
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (ActivityNotFoundException) { }
    }

    private static void OpenBatteryList(Context context)
    {
        try
        {
            var intent = new Intent(Settings.ActionIgnoreBatteryOptimizationSettings);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (ActivityNotFoundException) { OpenSettings(context); }
    }

    /// <summary>Le réglage à vérifier selon la marque, là où Android seul ne suffit pas.</summary>
    public static string HintFor(string? manufacturer) => (manufacturer ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "tecno" or "infinix" or "itel" =>
            "Sur ce téléphone, ouvrez aussi Réglages, Gestion des applications au démarrage : mettez SCOLIA en manuel et autorisez tout.",
        "xiaomi" or "redmi" or "poco" =>
            "Sur ce téléphone, activez aussi le démarrage automatique de SCOLIA et choisissez « Aucune restriction » dans Économiseur de batterie.",
        "huawei" or "honor" =>
            "Sur ce téléphone, ouvrez aussi Batterie, Lancement d'applications : SCOLIA en gestion manuelle, tout autorisé.",
        "samsung" =>
            "Sur ce téléphone, mettez aussi SCOLIA dans « Applications jamais en veille », dans Batterie.",
        "oppo" or "realme" or "oneplus" or "vivo" =>
            "Sur ce téléphone, autorisez aussi le démarrage automatique et l'activité en arrière-plan de SCOLIA, dans Batterie.",
        _ => string.Empty,
    };
}
