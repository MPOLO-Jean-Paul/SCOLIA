using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scolia.Infrastructure.Persistence;
using Scolia.Network;

namespace Scolia.Infrastructure.Network;

/// <summary>
/// Annonce du serveur sur le réseau local : répond aux demandes de découverte des postes SCOLIA.
/// Actif seulement quand l'accès réseau local est ouvert, et seulement pour les postes du réseau de l'établissement.
/// </summary>
public sealed class LanAnnouncer(IConfiguration configuration, IServiceScopeFactory scopes, ILogger<LanAnnouncer> logger) : BackgroundService
{
    private static readonly string Version = typeof(LanAnnouncer).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Scolia:Reseau:AccesReseauLocal", false)) return;
        var discoveryPort = configuration.GetValue("Scolia:Reseau:PortDecouverte", LanProtocol.DiscoveryPort);
        var httpPort = configuration.GetValue("Scolia:Reseau:Port", 5080);

        using var socket = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, discoveryPort));
        }
        catch (SocketException ex)
        {
            // Sans découverte, les postes peuvent toujours se connecter par l'adresse du serveur.
            logger.LogWarning(ex, "Découverte réseau indisponible : le port UDP {Port} est occupé.", discoveryPort);
            return;
        }
        logger.LogInformation("Découverte réseau active sur le port UDP {Port}.", discoveryPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try { received = await socket.ReceiveAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }

            if (!LanProtocol.IsNeighbour(received.RemoteEndPoint.Address)) continue;
            if (LanProtocol.ParseRequest(received.Buffer) is not { } nonce) continue;
            try
            {
                var reply = LanProtocol.Reply(await AnnouncementAsync(nonce, httpPort, stoppingToken));
                await socket.SendAsync(reply, received.RemoteEndPoint, stoppingToken);
            }
            catch (Exception ex) when (ex is SocketException or DbUpdateException or InvalidOperationException)
            {
                logger.LogDebug(ex, "Réponse de découverte non envoyée à {Remote}.", received.RemoteEndPoint);
            }
        }
    }

    private async Task<LanAnnouncement> AnnouncementAsync(string nonce, int httpPort, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScoliaDbContext>();
        var school = await db.Schools.AsNoTracking().OrderBy(s => s.CreatedAtUtc).Select(s => new { s.Id, s.Name }).FirstOrDefaultAsync(ct);
        return new LanAnnouncement(nonce, school?.Id.ToString() ?? string.Empty, school?.Name ?? "SCOLIA (installation à terminer)",
            Environment.MachineName, httpPort, Version);
    }
}
