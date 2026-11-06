using Material.Icons;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>
/// Centre Réseau autonome dans la navigation principale : accès direct aux
/// postes liés, à la découverte, au jumelage et aux opérations en attente.
/// Toutes les actions réutilisent les vérifications d'autorisation existantes.
/// </summary>
public sealed class NetworkHubPage : PageViewModel
{
    public NetworkHubPage(ShellViewModel shell) : base(shell)
    {
        Connection = new PosteViewModel(shell);
        OpenActivity = new RelayCommand(shell.ShowNetwork);
        Action("Réseau et activité", MaterialIconKind.LanConnect,
            () => { shell.ShowNetwork(); return Task.CompletedTask; }, primary: true);
    }

    public override string Title => "Réseau et postes";
    public override string Subtitle => "Connexions, appareils reliés et synchronisation";
    public override MaterialIconKind Glyph => MaterialIconKind.LanConnect;
    public PosteViewModel Connection { get; }
    public RelayCommand OpenActivity { get; }
    public override Task LoadAsync()
    {
        Connection.Refresh();
        return Task.CompletedTask;
    }
}
