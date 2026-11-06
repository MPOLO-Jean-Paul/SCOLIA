using Avalonia.Media.Imaging;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Une voie par laquelle l'autre appareil peut joindre ce serveur, montrée sous le code.</summary>
public sealed record PairingRouteVm(Material.Icons.MaterialIconKind Icon, string Title, string Detail);

/// <summary>
/// « Relier un appareil » sur le serveur : un grand code QR que l'autre appareil scanne. Il contient les adresses
/// du Wi-Fi local et l'invitation Render si elle est active ; aucun mot de passe.
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
        var localAvailable = Platform.IsMobile || PosteAdmin.LanEnabled();
        Shared = localAvailable || invitation is not null;
        Code = PairingCode.ForThisServer(school, machine, localAvailable ? PosteAdmin.Addresses() : [], invitation);
        School = school;
        CodeText = string.Empty;
        Image = null;
        Renew = new AsyncCommand(RenewInvitationAsync);
        _ = RenewInvitationAsync();

        var routes = new List<PairingRouteVm>();
        foreach (var lan in Code.LanAddresses)
            routes.Add(new(Material.Icons.MaterialIconKind.Wifi, "Wi-Fi de l'établissement", $"{lan.Host}:{lan.Port}"));
        if (Code.HasInvitation)
            routes.Add(new(Material.Icons.MaterialIconKind.Web, "Internet", $"Invitation valable jusqu'au {Format.Date(DateOnly.FromDateTime(remote!.Link.ExpiresUtc.ToLocalTime()))}"));
        Routes = routes;

        Copy = new AsyncCommand(async () =>
        {
            if (!Ready) return;
            if (shell.CopyHandler is { } copy) await copy(CodeText);
            shell.Notify("Invitation copiée : utilisez-la sur l'autre appareil avant son expiration.");
        });
        Close = new RelayCommand(shell.CloseDialog);
    }

    private sealed record InvitationReply(string Token, DateTime ExpiresUtc);
    public PairingCode Code { get; private set; }
    public string School { get; }
    public string CodeText { get; private set; }
    public Bitmap? Image { get; private set; }
    public bool Ready => Image is not null && Code.AccessTicket is not null;
    public string ProgressText { get; private set; } = "Préparation d'une invitation sécurisée…";
    public bool CanInvite => shell.IsServerComputer && shell.Can("utilisateurs.gerer");
    public AsyncCommand Renew { get; }

    private async Task RenewInvitationAsync()
    {
        if (!CanInvite)
        {
            ProgressText = "Seul un responsable connecté sur le serveur peut inviter un appareil.";
            Raise(nameof(ProgressText));
            return;
        }
        ProgressText = "Génération d'un accès à usage unique…";
        Raise(nameof(ProgressText));
        CodeText = string.Empty;
        Image = null;
        Raise(nameof(Image));
        Raise(nameof(Ready));
        try
        {
            var invite = await shell.Api.SendAsync<InvitationReply>(HttpMethod.Post, "api/jumelage/inviter", new { });
            Code = Code with { AccessTicket = invite.Token };
            if (Code.Addresses.Count == 0 && !Code.HasInvitation)
            {
                // Un QR composé uniquement d'un ticket, sans adresse de serveur,
                // paraît valable visuellement mais ne permet aucun jumelage.
                ProgressText = "Le partage réseau est désactivé : activez le réseau local ou la connexion Internet Render, puis générez un nouveau QR.";
                Raise(nameof(ProgressText));
                Raise(nameof(Ready));
                return;
            }
            CodeText = Code.Text();
            Image = QrCodes.Render(CodeText);
            ProgressText = "Code valable 5 minutes et utilisable une seule fois. Connexion locale ou Internet automatique.";
            Raise(nameof(Code));
            Raise(nameof(CodeText));
            Raise(nameof(Image));
            Raise(nameof(Ready));
        }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            ProgressText = "Impossible de générer l'invitation. Vérifiez l'accès au serveur et vos autorisations.";
        }
        Raise(nameof(ProgressText));
    }
    public IReadOnlyList<PairingRouteVm> Routes { get; private set; }
    public bool HasRoutes => Routes.Count > 0;

    /// <summary>Ordinateur dont le partage est fermé : aucun autre appareil ne pourrait le joindre.</summary>
    public bool Shared { get; private set; }
    public bool NotShared => !Shared;

    public string Title => "Relier un autre appareil";
    public string Steps => Platform.IsMobile
        ? "Sur l'autre appareil : Changer d'ordinateur principal, puis Scanner le code QR."
        : "Sur l'autre appareil : Changer d'ordinateur principal, puis Scanner le code QR. Sur un ordinateur, collez le code copié.";
    public string Footer => "Ce QR autorise temporairement un autre appareil à ouvrir ce compte avec les mêmes droits. Ne le partagez qu\u2019avec un appareil de confiance. Vous pourrez révoquer sa liaison.";

    public AsyncCommand Copy { get; }
    public RelayCommand Close { get; }
}
