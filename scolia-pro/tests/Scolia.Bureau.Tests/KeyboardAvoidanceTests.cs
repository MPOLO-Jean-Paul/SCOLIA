using Avalonia;
using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Place réservée au clavier du téléphone, selon que la fenêtre a déjà été réduite par Android ou non.</summary>
public sealed class KeyboardAvoidanceTests
{
    [Fact]
    public void Ecran_bord_a_bord_reserve_toute_la_hauteur_du_clavier()
    {
        // Android 15 et plus : l'écran garde sa hauteur, le clavier couvre ses 300 derniers points.
        Assert.Equal(300, KeyboardAvoidance.Overlap(760, new Rect(0, 460, 360, 300), alreadyShrunk: 0));
    }

    [Fact]
    public void Fenetre_deja_reduite_par_android_ne_reserve_rien_de_plus()
    {
        // Téléphone plus ancien : Android a déjà réduit l'écran de 300 points au-dessus du clavier.
        Assert.Equal(0, KeyboardAvoidance.Overlap(460, new Rect(0, 160, 360, 300), alreadyShrunk: 300));
    }

    [Fact]
    public void Clavier_ferme_ou_flottant_ne_reserve_rien()
    {
        Assert.Equal(0, KeyboardAvoidance.Overlap(760, default, alreadyShrunk: 0));
        Assert.Equal(0, KeyboardAvoidance.Overlap(400, new Rect(0, 460, 360, 300), alreadyShrunk: 0));
    }
}
