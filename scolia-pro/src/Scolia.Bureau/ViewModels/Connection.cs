using System.Collections.ObjectModel;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Bureau.ViewModels;

/// <summary>Serveur proposé dans la liste, avec son bouton « Rejoindre ».</summary>
public sealed class ServerRowVm(DiscoveredServer server, Func<DiscoveredServer, Task> join)
{
    public DiscoveredServer Server { get; } = server;
    public string School => Server.School;
    public string Detail => Server.Detail;
    public AsyncCommand Join { get; } = new(() => join(server));
}

/// <summary>
/// Choix du serveur de l'établissement : les SCOLIA trouvés sur le réseau, ou cet ordinateur comme serveur.
/// N'apparaît que si le choix n'est pas évident (plusieurs serveurs, ou serveur mémorisé injoignable).
/// </summary>
public sealed class ConnectionViewModel : Observable
{
    private readonly ShellViewModel shell;
    private string message = string.Empty;
    private bool searching;

    public ConnectionViewModel(ShellViewModel shell)
    {
        this.shell = shell;
        Search = new AsyncCommand(SearchAsync);
        UseThisComputer = new AsyncCommand(() => shell.UseThisComputerAsync());
    }

    public ObservableCollection<ServerRowVm> Servers { get; } = [];
    public bool HasServers => Servers.Count > 0;
    public string Message { get => message; private set => Set(ref message, value); }
    public bool Searching { get => searching; private set { if (Set(ref searching, value)) Raise(nameof(NotSearching)); } }
    public bool NotSearching => !searching;

    public AsyncCommand Search { get; }
    public AsyncCommand UseThisComputer { get; }

    /// <summary>Affiche une liste déjà obtenue, avec le message qui explique la situation.</summary>
    public void Show(IReadOnlyList<DiscoveredServer> servers, string text)
    {
        Servers.Clear();
        foreach (var server in servers) Servers.Add(new ServerRowVm(server, shell.JoinAsync));
        Raise(nameof(HasServers));
        Message = text;
    }

    public async Task SearchAsync()
    {
        Searching = true;
        Message = "Recherche de SCOLIA sur le réseau de l'établissement…";
        try
        {
            var servers = await LanDiscovery.FindAsync(TimeSpan.FromSeconds(2.5));
            Show(servers, servers.Count switch
            {
                0 => "Aucun SCOLIA ne répond sur ce réseau. Vérifiez que l'ordinateur principal est allumé et que SCOLIA y est ouvert.",
                1 => "SCOLIA a été trouvé sur le réseau.",
                _ => "Plusieurs SCOLIA répondent sur ce réseau : choisissez celui de votre établissement.",
            });
        }
        finally { Searching = false; }
    }
}
