namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Mise en page du téléphone, connue de toute l'interface : la fenêtre la fixe d'après sa largeur, avant que les
/// vues ne soient construites. Les vues construites ensuite prennent leur forme de téléphone : cadre à onglets,
/// tableaux en cartes, filtres sur toute la largeur.
/// </summary>
public static class PhoneLayout
{
    public static bool Active { get; private set; }

    /// <summary>Change la mise en page ; renvoie vrai si elle a changé.</summary>
    public static bool Set(bool active)
    {
        if (Active == active) return false;
        Active = active;
        return true;
    }
}
