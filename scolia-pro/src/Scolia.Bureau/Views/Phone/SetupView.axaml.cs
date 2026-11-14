using Avalonia.Controls;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.Views.Phone;

/// <summary>Configuration de l'établissement sur le téléphone, quand il en devient le serveur : une colonne, boutons en bas.</summary>
public partial class SetupView : UserControl
{
    public SetupView()
    {
        InitializeComponent();
        // Marges et cartes suivent la taille de l'écran : aucun défilement horizontal sur un petit téléphone.
        AuthLayout.Track(SetupRoot);
    }
}
