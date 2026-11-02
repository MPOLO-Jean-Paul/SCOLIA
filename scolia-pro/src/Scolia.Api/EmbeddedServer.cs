namespace Scolia.Api;

/// <summary>
/// Serveur SCOLIA démarré dans l'application elle-même : sur un téléphone qui sert d'ordinateur principal, quand
/// l'établissement n'a pas d'ordinateur. Il écoute sur le Wi-Fi, s'annonce aux autres postes et garde ses données
/// dans le dossier de l'application.
/// </summary>
public sealed class EmbeddedServer
{
    private WebApplication? app;

    public required string DataDirectory { get; init; }
    public int Port { get; init; } = 5080;

    /// <summary>Nom annoncé aux autres postes, celui du téléphone : « Samsung Galaxy A15 ».</summary>
    public string DeviceName { get; init; } = Environment.MachineName;

    /// <summary>Services propres à l'appareil : sur Android, les documents PDF sont dessinés sans QuestPDF.</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    /// <summary>Port UDP de la découverte des postes ; celui de SCOLIA par défaut.</summary>
    public int? DiscoveryPort { get; init; }

    public bool IsRunning => app is not null;

    internal IEnumerable<KeyValuePair<string, string?>> Settings() =>
    [
        new("Scolia:Stockage:DataDirectory", DataDirectory),
        new("Scolia:Reseau:Port", Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new("Scolia:Reseau:AccesReseauLocal", "true"),
        new("Scolia:Reseau:NomPoste", DeviceName),
        new("Scolia:Reseau:TypePoste", "telephone"),
        new("Scolia:OuvrirNavigateur", "false"),
        new("Scolia:Icone", "false"),
        .. DiscoveryPort is int udp ? [new KeyValuePair<string, string?>("Scolia:Reseau:PortDecouverte", udp.ToString(System.Globalization.CultureInfo.InvariantCulture))] : Array.Empty<KeyValuePair<string, string?>>(),
    ];

    /// <summary>Démarre le serveur s'il ne tourne pas : base créée ou mise à jour, puis écoute sur le réseau.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (app is not null) return;
        Directory.CreateDirectory(DataDirectory);
        var created = await ScoliaHost.CreateAsync([], this) ?? throw new InvalidOperationException("Le serveur SCOLIA n'a pas pu être préparé.");
        try { await created.StartAsync(ct); }
        catch
        {
            await created.DisposeAsync();
            throw;
        }
        app = created;
    }

    public async Task StopAsync()
    {
        if (app is not { } running) return;
        app = null;
        try { await running.StopAsync(); }
        finally { await running.DisposeAsync(); }
    }
}
