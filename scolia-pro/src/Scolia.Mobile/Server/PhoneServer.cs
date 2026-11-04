using Android.Content;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Api;
using Scolia.Application.Abstractions;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile.Server;

/// <summary>
/// Serveur scolaire local, partagé par la fenêtre Android et son service au premier plan.
/// Les opérations de démarrage/arrêt sont mises en file afin de ne jamais ouvrir deux
/// serveurs sur le même port, même après une reprise Android ou un clic rapide.
/// </summary>
public sealed class PhoneServer(Context context) : ILocalServer
{
    private static readonly object SharedGate = new();
    private static PhoneServer? shared;
    public static PhoneServer Get(Context context)
    {
        lock (SharedGate)
            return shared ??= new PhoneServer(context.ApplicationContext ?? context);
    }

    private readonly object gate = new();
    private EmbeddedServer? server;
    private Task lifecycle = Task.CompletedTask;
    private bool shouldHost;
    // Une commande explicite de changement de serveur ne doit pas être annulée par
    // un OnResume Android arrivé pendant l'ouverture des réglages ou du VPN.
    private bool explicitlyStopped;

    // La présence de la notification ne prouve pas que Kestrel accepte les connexions.
    // Le diagnostic est exécuté par le service Android, même si l'activité n'est plus visible.
    private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromSeconds(4) };
    private int consecutiveHealthFailures;
    private int healthProbeInProgress;
    private bool? lastLocalHealth;
    private string lastDiagnostic = "Diagnostic non effectué. Vérifiez le serveur depuis Poste et réseau.";

    /// <summary>État réel de l'API après les vérifications réseau de ce téléphone.</summary>
    public string LastDiagnostic
    {
        get { lock (gate) return lastDiagnostic; }
    }

    private void SetDiagnostic(string message)
    {
        lock (gate) lastDiagnostic = $"{DateTime.Now:HH:mm:ss} · {message}";
        ServeurScolia.UpdateServerNotification(message);
        Android.Util.Log.Info("SCOLIA-Serveur", message);
    }

    /// <summary>Vrai seulement si l'API locale écoute effectivement, pas sur la seule présence de la notification.</summary>
    public bool IsServing
    {
        get { lock (gate) return shouldHost && server?.IsRunning == true; }
    }

    /// <summary>Ne jamais déclarer l'API « vérifiée » à partir de la notification seule.</summary>
    public bool IsServingVerified
    {
        get { lock (gate) return shouldHost && server?.IsRunning == true && lastLocalHealth == true; }
    }

    /// <summary>
    /// Retour de l'interface : réparer un service de premier plan disparu sans recréer
    /// la base, ni attendre que l'utilisateur se reconnecte. Minimiser SCOLIA,
    /// verrouiller Android ou ouvrir Tailscale ne sont jamais des commandes Stop().
    /// </summary>
    public void EnsureServingOnResume()
    {
        if (ConnectionStore.Load()?.Mode != ConnectionSettings.Local) return;
        lock (gate)
        {
            if (explicitlyStopped) return;
            shouldHost = true;
            if (!ServeurScolia.IsActive)
            {
                try { ServeurScolia.Begin(context); }
                catch (Exception ex) when (ex is Java.Lang.RuntimeException or InvalidOperationException
                    or System.ComponentModel.Win32Exception)
                {
                    Android.Util.Log.Error("SCOLIA", $"Réactivation du service serveur refusée : {ex.Message}");
                    return;
                }
            }
            if (server?.IsRunning != true) Enqueue(StartEmbeddedAsync);
        }
    }

    /// <summary>
    /// Surveillance exécutée par le service Android et non par la fenêtre Avalonia :
    /// si une panne locale ferme l'API pendant que le téléphone est verrouillé, tenter
    /// de la remettre en service. N'ouvre ni activité, ni VPN, ni second serveur.
    /// </summary>
    internal void EnsureServingFromForegroundService()
    {
        if (ConnectionStore.Load()?.Mode != ConnectionSettings.Local) return;
        lock (gate)
        {
            if (explicitlyStopped || server?.IsRunning == true) return;
            shouldHost = true;
            Enqueue(StartEmbeddedAsync);
        }
    }

    /// <summary>
    /// Vérifie réellement l'API locale, pas seulement l'objet .NET encore en mémoire.
    /// Après trois échecs consécutifs, redémarre uniquement l'hébergement local :
    /// aucun dossier, aucune session ni base scolaire n'est supprimé.
    /// Lorsque le réseau revient, rétablit aussi le relais distant si nécessaire.
    /// </summary>
    internal async Task VerifyAndRecoverInBackgroundAsync()
    {
        if (ConnectionStore.Load()?.Mode != ConnectionSettings.Local) return;
        if (Interlocked.Exchange(ref healthProbeInProgress, 1) != 0) return;
        try
        {
            if (!IsServing)
            {
                lock (gate) lastLocalHealth = false;
                SetDiagnostic("Serveur non prêt : tentative de démarrage ou de récupération de l'API.");
                EnsureServingFromForegroundService();
                return;
            }

            bool healthy;
            try
            {
                using var response = await ProbeClient.GetAsync(
                    new Uri($"http://127.0.0.1:{ConnectionStore.LocalAddress.Port}/api/sante"))
                    .ConfigureAwait(false);
                healthy = response.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                healthy = false;
            }

            lock (gate) lastLocalHealth = healthy;

            // Une API accessible sur 127.0.0.1 n'est pas forcément accessible
            // par les autres ordinateurs : contrôler également une adresse IPv4
            // de l'appareil, sans confondre le résultat avec un test depuis un PC.
            bool? networkAddressHealthy = null;
            if (healthy)
            {
                try
                {
                    var addresses = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                                      nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                        .Select(entry => entry.Address)
                        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork &&
                                     !IPAddress.IsLoopback(ip) &&
                                     !ip.GetAddressBytes().AsSpan(0, 2).SequenceEqual(new byte[] { 169, 254 }))
                        .Take(2)
                        .ToArray();
                    if (addresses.Length > 0)
                    {
                        networkAddressHealthy = false;
                        foreach (var address in addresses)
                        {
                            try
                            {
                                using var response = await ProbeClient.GetAsync(
                                    new Uri($"http://{address}:{ConnectionStore.LocalAddress.Port}/api/sante"))
                                    .ConfigureAwait(false);
                                if (response.IsSuccessStatusCode)
                                {
                                    networkAddressHealthy = true;
                                    break;
                                }
                            }
                            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
                            { /* Adresse de cet appareil non accessible par auto-test. */ }
                        }
                    }
                }
                catch (Exception ex) when (ex is NetworkInformationException or
                    PlatformNotSupportedException or UnauthorizedAccessException or SocketException)
                { Android.Util.Log.Warn("SCOLIA-Serveur", $"Test d'adresse réseau indisponible : {ex.GetType().Name}"); }
            }

            if (healthy)
                SetDiagnostic(networkAddressHealthy switch
                {
                    true => "API locale et adresse réseau du téléphone : accessibles. Tester la liaison depuis le PC.",
                    false => "API locale accessible, adresse réseau non vérifiée. Contrôler Wi-Fi, partage ou VPN depuis le PC.",
                    _ => "API locale accessible. Test réseau externe à effectuer depuis un autre appareil."
                });
            else
                SetDiagnostic("API scolaire locale injoignable : vérification et récupération en cours.");

            bool shouldRestart = false;
            lock (gate)
            {
                if (!shouldHost || explicitlyStopped) return;
                consecutiveHealthFailures = healthy ? 0 : consecutiveHealthFailures + 1;
                if (consecutiveHealthFailures >= 3)
                {
                    consecutiveHealthFailures = 0;
                    shouldRestart = true;
                    Enqueue(async () =>
                    {
                        await StopEmbeddedAsync().ConfigureAwait(false);
                        await StartEmbeddedAsync().ConfigureAwait(false);
                    });
                }
            }
            if (shouldRestart)
                Android.Util.Log.Warn("SCOLIA-Serveur", "API injoignable trois fois : reprise locale sans suppression des données.");

            if (healthy)
            {
                // Un changement Wi-Fi / 4G / VPN n'est jamais une demande d'arrêt.
                try { RemoteConnection.StartHostIfMissing(ConnectionStore.LocalAddress); }
                catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException)
                { Android.Util.Log.Warn("SCOLIA-Serveur", $"Relais distant à reprendre : {ex.GetType().Name}"); }
            }
        }
        finally { Interlocked.Exchange(ref healthProbeInProgress, 0); }
    }

    public string DataDirectory => Path.Combine(context.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "serveur");

    public void Start() => StartCore(startForegroundService: true);

    // Appelé seulement par le service Android déjà en avant-plan.
    internal void RestoreAfterServiceRestart()
    {
        if (ConnectionStore.Load()?.Mode != ConnectionSettings.Local) return;
        lock (gate)
        {
            // Le service peut délivrer un événement tardif après un choix
            // explicite « rejoindre un autre établissement ». Le lock est
            // réentrant et empêche Stop() de s'intercaler entre les deux étapes.
            if (!explicitlyStopped) StartCore(startForegroundService: false);
        }
    }

    private void StartCore(bool startForegroundService)
    {
        lock (gate)
        {
            shouldHost = true;
            explicitlyStopped = false;
            if (startForegroundService)
            {
                ServeurScolia.Begin(context);
                // L'autorisation batterie doit être demandée depuis l'activité visible
                // (MainActivity.OnResume), jamais par un service déjà en arrière-plan.
            }
            Enqueue(StartEmbeddedAsync);
        }
    }

    public void Restart()
    {
        lock (gate)
        {
            shouldHost = true;
            explicitlyStopped = false;
            Enqueue(async () =>
            {
                await StopEmbeddedAsync().ConfigureAwait(false);
                await StartEmbeddedAsync().ConfigureAwait(false);
            });
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            shouldHost = false;
            explicitlyStopped = true;
            consecutiveHealthFailures = 0;
            lastLocalHealth = false;
            Enqueue(async () =>
            {
                await StopEmbeddedAsync().ConfigureAwait(false);
                bool stopNotification;
                lock (gate) stopNotification = !shouldHost;
                if (stopNotification) ServeurScolia.End(context);
            });
        }
    }

    private void Enqueue(Func<Task> action)
    {
        // Le fil UI et le service Android peuvent demander un démarrage en même temps.
        // Chaque étape attend strictement la précédente (et traite ses erreurs).
        var previous = lifecycle;
        lifecycle = Task.Run(async () =>
        {
            try { await previous.ConfigureAwait(false); }
            catch (Exception ex) { Android.Util.Log.Error("SCOLIA", $"Cycle serveur précédent : {ex}"); }
            try { await action().ConfigureAwait(false); }
            catch (Exception ex) { Android.Util.Log.Error("SCOLIA", $"Cycle serveur : {ex}"); }
        });
    }

    private async Task StartEmbeddedAsync()
    {
        lock (gate)
        {
            if (!shouldHost || server is not null) return;
            lastLocalHealth = null;
        }

        var created = new EmbeddedServer
        {
            DataDirectory = DataDirectory,
            Port = ConnectionStore.LocalAddress.Port,
            DeviceName = Platform.DeviceName ?? "Téléphone",
            ConfigureServices = services => services.AddSingleton<IDocumentService, SkiaDocumentService>(),
        };
        try
        {
            await created.StartAsync().ConfigureAwait(false);
            // Si l'utilisateur a changé d'établissement pendant le démarrage,
            // fermer le serveur créé au lieu de le rendre accessible.
            bool keep;
            lock (gate)
            {
                keep = shouldHost;
                if (keep) server = created;
            }
            if (!keep) await created.StopAsync().ConfigureAwait(false);
            else
            {
                // Restaurer aussi le relais Internet (s'il avait été activé) :
                // la reconnexion ne doit ni arrêter le serveur ni effacer sa base.
                try { RemoteConnection.StartHostIfMissing(ConnectionStore.LocalAddress); }
                catch (Exception ex)
                { Android.Util.Log.Warn("SCOLIA", $"Relais distant non restauré : {ex.GetType().Name}"); }
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Error("SCOLIA", $"Démarrage serveur impossible : {ex}");
            try { await created.StopAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { Android.Util.Log.Warn("SCOLIA", $"Nettoyage serveur : {cleanup.Message}"); }
            bool stopNotification;
            lock (gate) stopNotification = !shouldHost;
            if (stopNotification) ServeurScolia.End(context);
        }
    }

    private async Task StopEmbeddedAsync()
    {
        EmbeddedServer? stopping;
        lock (gate)
        {
            stopping = server;
            server = null;
            lastLocalHealth = false;
        }
        if (stopping is not null) await stopping.StopAsync().ConfigureAwait(false);
    }
}
