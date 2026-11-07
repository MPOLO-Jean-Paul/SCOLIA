using System.Collections.ObjectModel;
using Scolia.Bureau.Infrastructure;
using Scolia.Transport;

namespace Scolia.Bureau.ViewModels;

/// <summary>Poste autorisé sur le serveur, indépendamment de l'état d'Internet.</summary>
public sealed class AuthorizedDeviceRowVm
{
    public AuthorizedDeviceRowVm(Guid id, string name, Func<Guid, Task> revoke)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "Appareil SCOLIA" : name;
        Remove = new AsyncCommand(() => revoke(Id));
    }
    public Guid Id { get; }
    public string Name { get; }
    public string Detail => "Autorisé · Reconnexion automatique après une coupure";
    public AsyncCommand Remove { get; }
}

/// <summary>Gestion réservée au secrétariat sur l'appareil principal. La comptabilité rejoint par invitation.</summary>
public sealed class RemoteConnectionViewModel : Observable
{
    private readonly ShellViewModel shell;
    private string relay = RemoteLink.DefaultRelay;
    private string invitation = string.Empty;
    private string message = string.Empty;
    /// <param name="prepareInvitation">Vrai dans « Réseau et activité » : une invitation de compte est préparée à l'ouverture.</param>
    public RemoteConnectionViewModel(ShellViewModel shell, bool prepareInvitation = true)
    {
        this.shell = shell;
        if (RemoteConnection.Load() is { } current)
        {
            relay = current.Link.Relay;
            if (prepareInvitation && current.Host && current.Enabled) _ = PrepareInvitationAsync(current.Link);
        }
        Enable = new AsyncCommand(EnableAsync);
        RefreshDevices = new AsyncCommand(RefreshDevicesAsync);
        if (CanManage) _ = RefreshDevicesAsync();
        RenewAccess = new AsyncCommand(async () =>
        {
            if (CanManage && RemoteConnection.Load() is { Host: true, Enabled: true } current)
                await PrepareInvitationAsync(current.Link);
        });
        Disable = new AsyncCommand(async () =>
        {
            if (!CanManage || !IsActive) return;
            // Les appareils reliés par Internet perdent l'accès : demander confirmation.
            if (!await shell.Confirm("Désactiver l'accès par Internet ?",
                    "Les appareils qui ne sont pas sur le Wi-Fi de l'établissement ne pourront plus joindre ce serveur. "
                    + "Pour les reconnecter, réactivez l'accès Internet : les appareils déjà autorisés n'ont pas besoin d'un nouveau code QR. Le partage Wi-Fi reste disponible.",
                    "Désactiver", danger: true))
                return;
            RemoteConnection.Disable();
            Invitation = string.Empty;
            Message = "Accès Internet temporairement désactivé. Les appareils autorisés restent mémorisés : ils se reconnecteront à la réactivation, sans nouveau QR.";
        });
    }

    /// <summary>Accès par Internet actif sur ce serveur : liaison activée et non expirée.</summary>
    public bool IsActive => RemoteConnection.Load() is { Host: true, Enabled: true } current && current.Link.ExpiresUtc > DateTime.UtcNow;
    public bool IsInactive => !IsActive;

    /// <summary>Nom du relais sans le protocole, pour l'afficher sans jargon technique.</summary>
    public string RelayHost => Uri.TryCreate(RemoteConnection.Load()?.Link.Relay ?? relay, UriKind.Absolute, out var uri) ? uri.Host : relay;

    public string StatusTitle => !IsActive ? "Désactivé"
        : RemoteConnection.Hosting ? "Actif" : "Actif · connexion au relais en cours";

    public string StatusLine => IsActive && RemoteConnection.Load() is { } current
        ? $"Les appareils autorisés joignent ce serveur depuis n'importe quel réseau, par le relais sécurisé {RelayHost}. "
            + $"Autorisation persistante jusqu'au retrait volontaire de l'appareil dans Réseau et activité."
        : "Les appareils hors du Wi-Fi de l'établissement ne peuvent pas joindre ce serveur. Activez l'accès pour les téléphones et ordinateurs distants.";

    public Avalonia.Media.IBrush StatusBrush => ThemeBrush.Get(!IsActive ? "ToneNeutral" : RemoteConnection.Hosting ? "ToneGreen" : "ToneOrange");
    public string EnableLabel => IsActive ? "Vérifier la connexion" : "Activer l'accès par Internet";

    public void RefreshStatus()
    {
        foreach (var name in new[] { nameof(IsActive), nameof(IsInactive), nameof(RelayHost), nameof(StatusTitle), nameof(StatusLine), nameof(StatusBrush), nameof(EnableLabel) })
            Raise(name);
    }
    public bool CanManage => shell.IsServerComputer && shell.Can("parametres.ecrire");

    public string Relay { get => relay; set => Set(ref relay, value); }
    public string Invitation { get => invitation; private set => Set(ref invitation, value); }
    public string Message { get => message; private set => Set(ref message, value); }
    public ObservableCollection<AuthorizedDeviceRowVm> Devices { get; } = [];
    public bool NoDevices => Devices.Count == 0;
    private string deviceStatus = "Liste des appareils autorisés sur ce serveur.";
    public string DeviceStatus { get => deviceStatus; private set => Set(ref deviceStatus, value); }
    public AsyncCommand RefreshDevices { get; }
    private sealed record DeviceReply(Guid Id, string Name, DateTime ExpiresUtc, bool Revoked);

    private async Task RefreshDevicesAsync()
    {
        if (!CanManage) return;
        try
        {
            var devices = await shell.Api.GetAsync<List<DeviceReply>>("api/jumelage/appareils");
            Devices.Clear();
            foreach (var device in devices.Where(d => !d.Revoked))
                Devices.Add(new AuthorizedDeviceRowVm(device.Id, device.Name, RemoveDeviceAsync));
            Raise(nameof(NoDevices));
            DeviceStatus = Devices.Count == 0
                ? "Aucun appareil autorisé. Affichez un QR pour en relier un."
                : $"{Devices.Count} appareil(s) autorisé(s) sur le serveur, même hors connexion.";
        }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            DeviceStatus = "Liste indisponible : vérifiez la connexion au serveur.";
        }
    }

    private async Task RemoveDeviceAsync(Guid id)
    {
        if (!CanManage) return;
        var item = Devices.FirstOrDefault(x => x.Id == id);
        if (item is null) return;
        if (!await shell.Confirm("Retirer cet appareil ?",
            $"L'appareil « {item.Name} » perdra immédiatement son accès Internet au serveur. Il faudra un nouveau QR pour le réautoriser.",
            "Retirer l'appareil")) return;
        try
        {
            await shell.Api.SendAsync(HttpMethod.Post, $"api/jumelage/appareils/{id}/revoquer", new { });
            Devices.Remove(item);
            Raise(nameof(NoDevices));
            DeviceStatus = $"{Devices.Count} appareil(s) toujours autorisé(s).";
            shell.Notify("Appareil retiré : son ancienne autorisation ne fonctionne plus.");
        }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            DeviceStatus = "Retrait non confirmé : vérifiez la connexion et actualisez la liste.";
        }
    }

    public AsyncCommand Enable { get; }
    public AsyncCommand RenewAccess { get; }
    private sealed record InvitationReply(string Token, DateTime ExpiresUtc);

    private async Task PrepareInvitationAsync(RemoteLink link)
    {
        if (!CanManage) return;
        try
        {
            var access = await shell.Api.SendAsync<InvitationReply>(HttpMethod.Post, "api/jumelage/inviter", new { });
            var addresses = Platform.IsMobile || PosteAdmin.LanEnabled() ? PosteAdmin.Addresses() : [];
            var qr = PairingCode.ForThisServer(shell.Login.SchoolName, DeviceIdentity.Name, addresses, link.Invitation())
                with { AccessTicket = access.Token };
            Invitation = qr.Text();
            Message = "Invitation de connexion automatique prête. Valable 5 minutes pour un seul appareil ; renouvelez-la pour le prochain.";
        }
        catch (Exception ex) when (ex is Api.ApiException or HttpRequestException or IOException or TaskCanceledException)
        {
            Invitation = string.Empty;
            Message = "Liaison Internet active, mais l'invitation de compte n'a pas été créée. Ouvrez « Relier un appareil » pour générer un nouveau QR.";
        }
    }

    public AsyncCommand Disable { get; }

    private async Task EnableAsync()
    {
        if (!CanManage) { Message = "Le secrétariat configure la liaison sur l'appareil principal."; return; }
        try
        {
            var previous = RemoteConnection.Load();
            // Un clic répété sur « Activer » ne doit jamais invalider les appareils
            // déjà autorisés. Rotation seulement après expiration, changement de
            // relais ou réactivation d'une connexion volontairement désactivée.
            var addressText = Relay.Trim();
            // Désactiver Internet met le relais en pause : cela ne révoque pas les
            // appareils et ne doit JAMAIS faire tourner la clé de la salle.
            var reuse = previous is { Host: true } &&
                string.Equals(previous.Link.Relay, addressText, StringComparison.OrdinalIgnoreCase);
            var link = reuse
                ? (previous!.Link.ExpiresUtc <= DateTime.UtcNow ? previous.Link.Resume() : previous.Link)
                : previous is { Host: true } ? previous.Link.Renew(addressText)
                : RemoteLink.Create(addressText);
            // Vérifier le relais avant de remplacer une liaison encore utilisable.
            var address = new UriBuilder(link.Relay) { Scheme = new Uri(link.Relay).Scheme == "wss" ? "https" : "http", Path = "/health" }.Uri;
            Message = "Vérification de la connexion Internet sécurisée et du certificat HTTPS…";
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(75) };
            using var response = await probe.GetAsync(address);
            response.EnsureSuccessStatusCode();
            RemoteConnection.Save(new RemoteConfiguration(link, true, shell.Login.SchoolName));
            if (reuse) RemoteConnection.StartHostIfMissing(shell.Api.Server);
            else RemoteConnection.StartHost(shell.Api.Server);
            if (reuse)
                Message = "Liaison Internet active ou rétablie. Les appareils autorisés restent jumelés et peuvent se synchroniser sans nouveau QR.";
            else
                await PrepareInvitationAsync(link);
        }
        catch (Exception ex) when (ex is FormatException or HttpRequestException or TaskCanceledException or IOException)
        { Message = "Accès non activé : vérifiez Internet, le certificat HTTPS et la disponibilité du relais. La configuration précédente est conservée."; }
        finally { RefreshStatus(); }
    }
}
