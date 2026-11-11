extern alias bureau;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using bureau::Scolia.Bureau.Api;
using bureau::Scolia.Bureau.Infrastructure;

namespace Scolia.Tests;

/// <summary>
/// Relais réseau entre le client et le serveur : l'éteindre revient à couper l'ordinateur principal ou le Wi-Fi,
/// le rallumer à son retour. Le serveur, lui, garde ses données.
/// </summary>
internal sealed class Relay : IDisposable
{
    private readonly int upstream;
    private TcpListener? listener;
    private readonly List<TcpClient> open = [];
    private CancellationTokenSource? stop;

    public Relay(int upstream)
    {
        this.upstream = upstream;
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        Port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
    }

    public int Port { get; }

    public void Start()
    {
        stop = new CancellationTokenSource();
        listener = new TcpListener(IPAddress.Loopback, Port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();
        _ = AcceptAsync(listener, stop.Token);
    }

    public void Stop()
    {
        stop?.Cancel();
        listener?.Stop();
        lock (open)
        {
            foreach (var client in open) client.Dispose();
            open.Clear();
        }
    }

    private async Task AcceptAsync(TcpListener server, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient inbound;
            try { inbound = await server.AcceptTcpClientAsync(ct); }
            catch (Exception) { return; }
            var outbound = new TcpClient();
            try { await outbound.ConnectAsync(IPAddress.Loopback, upstream, ct); }
            catch (Exception) { inbound.Dispose(); continue; }
            lock (open) { open.Add(inbound); open.Add(outbound); }
            _ = Pump(inbound.GetStream(), outbound.GetStream(), ct);
            _ = Pump(outbound.GetStream(), inbound.GetStream(), ct);
        }
    }

    private static async Task Pump(Stream from, Stream to, CancellationToken ct)
    {
        try { await from.CopyToAsync(to, ct); }
        catch (Exception) { }
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Coupure de l'ordinateur principal : le téléphone ou le poste continue sur ses données gardées, garde ses
/// opérations, puis les envoie à son retour sans doublon. Une opération refusée passe en conflit.
/// </summary>
public sealed class OfflineSyncTests : IDisposable
{
    private readonly ScoliaApp app = new();
    private readonly string folder = Path.Combine(Path.GetTempPath(), "scolia-hors-ligne", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        app.Dispose();
        try { Directory.Delete(folder, true); } catch (IOException) { }
    }

    private int StartServer()
    {
        app.UseKestrel(0);
        app.StartServer();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var port = new Uri(addresses.First()).Port;
        app.ClientOptions.BaseAddress = new Uri($"http://localhost:{port}");
        return port;
    }

    private static object Student(string lastName, string promotionId) => new
    {
        identity = new { lastName, middleName = "Kabeya", firstName = "Grace", birthPlace = "Lubumbashi", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
        promotionId, confirmDuplicate = false,
    };

    [Fact]
    public async Task Work_continues_offline_and_is_sent_once_when_the_server_returns()
    {
        var port = StartServer();
        var admin = await app.InstalledAdmin();
        var promotion = (await admin.Post("/api/etablissement/promotions", new
        {
            code = "OFF1", name = "Première année", level = 1, isActive = true, monthlyFee = 50000m, oneTimeFees = Array.Empty<object>(),
        }).Ok()).GetProperty("id").GetString()!;

        using var relay = new Relay(port);
        relay.Start();
        using var phone = new ScoliaClient(new Uri($"http://localhost:{relay.Port}/"));
        var session = await phone.LoginAsync("admin", "Scolia2026!");
        phone.Offline = new OfflineStore(phone.Server, folder) { User = session.UserName };
        phone.Offline.SaveCredential("admin", "Scolia2026!", session);

        // En ligne : la liste des promotions est lue, et gardée sur l'appareil.
        var online = await phone.GetAsync<List<PromotionRef>>("api/etablissement/promotions");
        Assert.Contains(online, p => p.Code == "OFF1");

        // L'ordinateur principal disparaît.
        relay.Stop();
        var cached = await phone.GetAsync<List<PromotionRef>>("api/etablissement/promotions");
        Assert.False(phone.IsOnline);
        Assert.Contains(cached, p => p.Code == "OFF1");

        // Une inscription faite hors ligne est gardée ; une action de réglage est refusée avec une explication.
        await Assert.ThrowsAsync<OfflineQueuedException>(() => phone.SendAsync(HttpMethod.Post, "api/apprenants", Student("HORSLIGNE", promotion)));
        await Assert.ThrowsAsync<OfflineException>(() => phone.SendAsync(HttpMethod.Post, "api/etablissement/promotions", new { code = "X" }));
        Assert.Single(phone.Offline.Outbox());

        // Le téléphone se rouvre sans réseau avec le dernier compte connecté, et pas avec un autre mot de passe.
        Assert.NotNull(phone.Offline.OfflineLogin("admin", "Scolia2026!"));
        Assert.Null(phone.Offline.OfflineLogin("admin", "mauvais"));

        // Le serveur revient : l'opération part, une seule fois.
        relay.Start();
        var item = phone.Offline.Outbox()[0];
        var (sent, refused) = await phone.FlushAsync();
        Assert.Equal((1, 0), (sent, refused));
        Assert.True(phone.IsOnline);
        Assert.Empty(phone.Offline.Outbox());

        // La réponse s'était perdue : le téléphone renvoie la même opération. Le serveur ne la refait pas.
        phone.Offline.Enqueue(item);
        Assert.Equal((1, 0), await phone.FlushAsync());
        var students = await admin.GetAsync("/api/apprenants?q=HORSLIGNE").Ok();
        Assert.Equal(1, students.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task An_operation_refused_by_the_server_on_return_becomes_a_conflict_to_review()
    {
        var port = StartServer();
        await app.InstalledAdmin();
        using var phone = new ScoliaClient(new Uri($"http://localhost:{port}/"));
        var session = await phone.LoginAsync("admin", "Scolia2026!");
        phone.Offline = new OfflineStore(phone.Server, folder) { User = session.UserName };

        // Une dépense sans espèces en caisse : le serveur la refuse à l'envoi.
        phone.Offline.Enqueue(new OutboxItem
        {
            Key = Guid.NewGuid().ToString("N"), Method = "POST", Path = "api/depenses", Label = "Dépense",
            Body = """{"category":"Divers","beneficiary":"X","description":"Trop","amount":999999,"method":"Especes"}""",
        });
        var (sent, refused) = await phone.FlushAsync();
        Assert.Equal((0, 1), (sent, refused));
        var conflict = Assert.Single(phone.Offline.Conflicts());
        Assert.False(string.IsNullOrWhiteSpace(conflict.Error));

        phone.Offline.Discard(conflict.Id);
        Assert.Empty(phone.Offline.Conflicts());
    }

    private sealed record PromotionRef(string Code);
}
