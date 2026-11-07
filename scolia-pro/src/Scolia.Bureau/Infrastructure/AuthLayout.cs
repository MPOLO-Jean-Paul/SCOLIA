using Avalonia;
using Avalonia.Controls;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Écrans d'accès (premier écran, jumelage, connexion, récupération du mot de passe, session verrouillée) :
/// un seul langage iOS clair, sur le téléphone comme sur l'ordinateur, et des tailles qui suivent l'écran.
/// <para>
/// Les classes posées sur l'hôte de la page permettent aux styles de réduire marges, polices et champs sur
/// les petits téléphones, de resserrer la hauteur quand l'écran est bas (clavier ouvert, paysage) et
/// d'élargir un peu la carte sur un grand écran, sans jamais créer de défilement horizontal.
/// </para>
/// </summary>
public static class AuthLayout
{
    /// <summary>Le style iOS est commun au téléphone et à l'ordinateur : une seule apparence, plus professionnelle.</summary>
    public static bool UseIosStyle { get; } = true;

    /// <summary>Largeur sous laquelle l'écran est un petit téléphone (marges et titres réduits).</summary>
    public const double CompactWidth = 390;

    /// <summary>Largeur sous laquelle l'écran est très étroit (polices et champs encore un peu réduits).</summary>
    public const double TinyWidth = 340;

    /// <summary>Hauteur sous laquelle les espaces verticaux se resserrent (clavier ouvert, petit écran, paysage).</summary>
    public const double ShortHeight = 680;

    /// <summary>Largeur à partir de laquelle la page dispose d'un grand écran (ordinateur, tablette).</summary>
    public const double WideWidth = 720;

    /// <summary>Suit la taille de l'hôte et pose les classes compact, tiny, short et wide.</summary>
    public static void Track(Control host, Action<Size>? changed = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.SizeChanged += (_, e) =>
        {
            Apply(host.Classes, e.NewSize);
            changed?.Invoke(e.NewSize);
        };
    }

    /// <summary>Classes d'adaptation pour une taille donnée ; une taille nulle (pas encore mesurée) ne change rien.</summary>
    public static void Apply(Classes classes, Size size)
    {
        ArgumentNullException.ThrowIfNull(classes);
        if (size.Width <= 0 || size.Height <= 0) return;
        classes.Set("compact", size.Width < CompactWidth);
        classes.Set("tiny", size.Width < TinyWidth);
        classes.Set("short", size.Height < ShortHeight);
        classes.Set("wide", size.Width >= WideWidth);
    }
}
