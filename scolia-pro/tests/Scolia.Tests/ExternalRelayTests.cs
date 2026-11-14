extern alias bureau;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using bureau::Scolia.Bureau.Api;
using bureau::Scolia.Bureau.Infrastructure;
using bureau::Scolia.Transport;

namespace Scolia.Tests;

/// <summary>
/// Relais réel (Cloudflare Worker lancé par « wrangler dev », ou relais déployé) : SCOLIA_RELAY_URL=ws://127.0.0.1:8787/.
/// Sans cette variable, le test ne fait rien. Vérifie l'hébergement, la connexion, le jumelage par code et la reconnexion.
/// </summary>
public sealed class ExternalRelayTests : IAsyncLifetime
{
    private static readonly string? RelayUrl = Environment.GetEnvironmentVariable("SCOLIA_RELAY_URL");
    private readonly ScoliaApp app = new();
    private HttpClient secretary = null!;

    public async Task InitializeAsync()
    {
        if (RelayUrl is null) return;
        app.UseKestrel(0);
        app.StartServer();
        app.ClientOptions.BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        secretary = await app.InstalledAdmin();
    }

    private static async Task WaitFor(RemoteHost host, TimeSpan delay)
    {
        using var timeout = new CancellationTokenSource(delay);
        try { while (!host.Connected) await Task.Delay(50, timeout.Token); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Hôte non confirmé par le relais : " + (host.LastConnectionFailure ?? "aucune erreur capturée"));
        }
    }

    [Fact]
    public async Task Hosting_login_pairing_and_quick_reconnection_through_the_real_relay()
    {
        if (RelayUrl is null) return;
        var link = RemoteLink.Create(RelayUrl);
        var host = new RemoteHost(link, app.ClientOptions.BaseAddress);
        await WaitFor(host, TimeSpan.FromSeconds(20));

        using (var phone = new ScoliaClient(link.VirtualServer))
        {
            phone.UseRemote(RemoteLink.Parse(link.Invitation()));
            // Jumelage par QR d'abord (preuve d'appareil exigée sur le relais), puis compte personnel.
            var ticket = (await secretary.Post("/api/jumelage/inviter", new { }).Ok()).GetProperty("token").GetString()!;
            var paired = await phone.AcceptDeviceInvitationAsync(ticket);
            Assert.NotEqual(Guid.Empty, paired.GrantId);
            var session = await phone.LoginAsync("admin", "Scolia2026!");
            Assert.Equal("admin", session.UserName);
        }

        // Coupure non détectée : l'ancienne session paraît encore ouverte quand le serveur revient.
        var stale = new RemoteHost(link, app.ClientOptions.BaseAddress);
        try { await WaitFor(stale, TimeSpan.FromSeconds(15)); }
        finally { stale.Dispose(); }
        await WaitFor(host, TimeSpan.FromSeconds(30));

        // Renouvellement de la session du serveur (toutes les 55 min, ou redémarrage de SCOLIA) :
        // la nouvelle session doit être acceptée tout de suite, même si l'ancienne paraît encore ouverte.
        host.Dispose();
        var again = new RemoteHost(link, app.ClientOptions.BaseAddress);
        try
        {
            await WaitFor(again, TimeSpan.FromSeconds(15));
            using var phone = new ScoliaClient(link.VirtualServer);
            phone.UseRemote(RemoteLink.Parse(link.Invitation()));
            Assert.True(await phone.IsRunningAsync());
        }
        finally { again.Dispose(); }
    }

    public Task DisposeAsync()
    {
        app.Dispose();
        return Task.CompletedTask;
    }
}
