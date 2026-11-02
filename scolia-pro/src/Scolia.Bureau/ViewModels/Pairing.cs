using Avalonia.Media.Imaging;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Une voie par laquelle l'autre appareil peut joindre ce serveur, montrée sous le code.</summary>
public sealed record PairingRouteVm(Material.Icons.MaterialIconKind Icon, string Title, string Detail);

/// <summary>
/// « Relier un appareil » sur le serveur : un grand code QR que l'autre appareil scanne. Il contient les adresses
/// du Wi-Fi, celle de Tailscale et l'invitation Internet si elle est active ; aucun mot de passe.
/// </summary>
public sealed class PairingViewModel : Observable
{
    private readonly ShellViewModel shell;

    public PairingViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        var school = shell.SchoolLine.Split(" · ")[0] is { Length: > 0 } name ? name : ConnectionStore.Load()?.School ?? "Établissement";
        var machine = Platform.DeviceName ?? Environment.MachineName;
        var remote = RemoteConnection.Load();
        var invitation = remote is { Host: true, Enabled: true } && remote.Link.ExpiresUtc > DateTime.UtcNow ? remote.Link.Invitation() : null;
        Shared = Platform.IsMobile || PosteAdmin.LanEnabled();
        Code = PairingCode.ForThisServer(school, machine, Shared ? PosteAdmin.Addresses() : [], invitation);
        School = school;
        CodeText = Code.Text();
        Image = QrCodes.Render(CodeText);

        var routes = new List<PairingRouteVm>();
        foreach (var lan in Code.LanAddresses)
            routes.Add(new(Material.Icons.MaterialIconKind.Wifi, "Wi-Fi de l'établissement", $"{lan.Host}:{lan.Port}"));
        foreach (var tailnet in Code.TailnetAddresses)
            routes.Add(new(Material.Icons.MaterialIconKind.ShieldLockOutline, "Tailscale, à distance", $"{tailnet.Host}:{tailnet.Port}"));
        if (Code.HasInvitation)
            routes.Add(new(Material.Icons.MaterialIconKind.Web, "Internet", $"Invitation valable jusqu'au {Format.Date(DateOnly.FromDateTime(remote!.Link.ExpiresUtc.ToLocalTime()))}"));
        Routes = routes;

        Copy = new AsyncCommand(async () =>
        {
            if (shell.CopyHandler is { } copy) await copy(CodeText);
            shell.Notify("Code de liaison copié : collez-le sur l'autre appareil, dans « Changer d'ordinateur principal ».");
        });
        Close = new RelayCommand(shell.CloseDialog);
    }

    public PairingCode Code { get; }
    public string School { get; }
    public string CodeText { get; }
    public Bitmap Image { get; }
    public IReadOnlyList<PairingRouteVm> Routes { get; }
    public bool HasRoutes => Routes.Count > 0;

    /// <summary>Ordinateur dont le partage est fermé : aucun autre appareil ne pourrait le joindre.</summary>
    public bool Shared { get; }
    public bool NotShared => !Shared;

    public string Title => "Relier un autre appareil";
    public string Steps => Platform.IsMobile
        ? "Sur l'autre appareil : Changer d'ordinateur principal, puis Scanner le code QR."
        : "Sur l'autre appareil : Changer d'ordinateur principal, puis Scanner le code QR. Sur un ordinateur, collez le code copié.";
    public string Footer => "Le code ne contient aucun mot de passe : le serveur valide l'appareil, puis chacun se connecte avec son compte.";

    public AsyncCommand Copy { get; }
    public RelayCommand Close { get; }
}
