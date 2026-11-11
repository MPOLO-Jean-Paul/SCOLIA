extern alias bureau;
extern alias relay;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using bureau::Scolia.Bureau.Api;
using bureau::Scolia.Bureau.Infrastructure;
using bureau::Scolia.Transport;
using RelayApplication = relay::Scolia.Relay.RelayApplication;

namespace Scolia.Tests;

/// <summary>Vraies sockets : deux clients, serveur scolaire SQLite, relais séparé et coupure réseau contrôlée.</summary>
public sealed class RemoteSyncTests : IAsyncLifetime
{
    private readonly ScoliaApp app = new();
    private WebApplication relayServer = null!;
    private RemoteHost host = null!;
    private RemoteLink link = null!;
    private HttpClient secretary = null!;
    private readonly string folder = Path.Combine(Path.GetTempPath(), "scolia-distant", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        app.UseKestrel(0);
        app.StartServer();
        app.ClientOptions.BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        secretary = await app.InstalledAdmin();
        relayServer = RelayApplication.Build(["--urls", "http://127.0.0.1:0"]);
        await relayServer.StartAsync();
        var relayUrl = relayServer.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        link = RemoteLink.Create(new UriBuilder(relayUrl) { Scheme = "ws" }.Uri.ToString());
        host = new RemoteHost(link, app.ClientOptions.BaseAddress);
        await WaitForHost();
    }

    private async Task WaitForHost()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (!host.Connected) await Task.Delay(30, timeout.Token);
    }

    private ScoliaClient Phone(RemoteLink? invitation = null)
    {
        var client = new ScoliaClient(link.VirtualServer);
        client.UseRemote(invitation ?? RemoteLink.Parse(link.Invitation()));
        return client;
    }

    [Fact]
    public async Task Existing_two_roles_and_server_permissions_are_preserved_through_relay()
    {
        var user = await secretary.Post("/api/utilisateurs", new { userName = "compta", displayName = "Comptabilité", role = "Comptabilite", password = "Provisoire1" }).Ok();
        using var phone = Phone();
        var session = await phone.LoginAsync("compta", "Provisoire1");
        Assert.Equal("Comptabilite", session.Role);
        await phone.GetAsync<JsonElement>("api/paiements");
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => phone.GetAsync<JsonElement>("api/utilisateurs"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => phone.SendAsync(HttpMethod.Put, "api/etablissement", new { name = "Interdit" }))).Status);
        var id = user.GetProperty("id").GetString();
        await secretary.Put($"/api/utilisateurs/{id}", new { displayName = "Comptable", role = "Comptabilite", isActive = false }).Ok();
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => phone.GetAsync<JsonElement>("api/paiements"))).Status);
    }

    [Fact]
    public async Task Wrong_pairing_secret_cannot_open_a_school_session()
    {
        using var phone = Phone(link with { ClientToken = new string('A', 64), HostToken = null });
        Assert.False(await phone.IsRunningAsync());
    }

    [Fact]
    public async Task Offline_registration_survives_host_interruption_and_is_applied_once_on_reconnection()
    {
        var promotion = await secretary.Post("/api/etablissement/promotions", new { code = "DST1", name = "Première", level = 1, isActive = true, monthlyFee = 50000m, oneTimeFees = Array.Empty<object>() }).Ok();
        using var phone = Phone();
        var session = await phone.LoginAsync("admin", "Scolia2026!");
        phone.Offline = new OfflineStore(phone.Server, folder) { User = session.UserName };
        await phone.GetAsync<JsonElement>("api/etablissement/promotions");
        host.Dispose();
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var input = new
        {
            identity = new { lastName = "DISTANT", middleName = "Kabeya", firstName = "Grace", birthPlace = "Lubumbashi", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId = promotion.GetProperty("id").GetString(), confirmDuplicate = false,
        };
        await Assert.ThrowsAsync<OfflineQueuedException>(() => phone.SendAsync(HttpMethod.Post, "api/apprenants", input));
        var pending = Assert.Single(phone.Offline.Outbox());
        host = new RemoteHost(link, app.ClientOptions.BaseAddress);
        await WaitForHost();
        Assert.Equal((1, 0), await phone.FlushAsync());
        phone.Offline.Enqueue(pending);
        Assert.Equal((1, 0), await phone.FlushAsync());
        var result = await secretary.GetAsync("/api/apprenants?q=DISTANT").Ok();
        Assert.Equal(1, result.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Payment_survives_lost_http_replay_cache_without_duplicate_receipt_or_amount()
    {
        var promotion = await secretary.Post("/api/etablissement/promotions", new { code = "PAYD", name = "Première", level = 1, isActive = true, monthlyFee = 50000m, oneTimeFees = Array.Empty<object>() }).Ok();
        var student = await secretary.Post("/api/apprenants", new
        {
            identity = new { lastName = "PAYEUR", middleName = "Kabeya", firstName = "Grace", birthPlace = "Lubumbashi", birthDate = new DateOnly(2007, 3, 14), gender = "F", guardianPhone = "0812345678", guardianName = "Parent" },
            promotionId = promotion.GetProperty("id").GetString(), confirmDuplicate = false,
        }).Ok();
        var enrollment = student.GetProperty("enrollments")[0].GetProperty("id").GetString()!;
        var (ids, amount) = await secretary.OpenMonths(enrollment, 1);
        using var phone = Phone();
        await phone.LoginAsync("admin", "Scolia2026!");
        var body = new { enrollmentId = enrollment, amount, feeItemIds = ids, method = "Especes", idempotencyKey = Guid.NewGuid().ToString("N") };
        var first = await phone.SendAsync<JsonElement>(HttpMethod.Post, "api/paiements", body);
        var cache = Path.Combine(app.DataDirectory, "idempotence");
        foreach (var file in Directory.EnumerateFiles(cache, "*.json")) File.Delete(file);
        var again = await phone.SendAsync<JsonElement>(HttpMethod.Post, "api/paiements", body);
        Assert.Equal(first.GetProperty("id").GetString(), again.GetProperty("id").GetString());
        var account = await secretary.GetAsync($"/api/inscriptions/{enrollment}/compte").Ok();
        Assert.Equal(amount, account.GetProperty("totalPaid").GetDecimal());
    }

    [Fact]
    public async Task Live_activity_stream_crosses_the_encrypted_relay()
    {
        using var phone = Phone();
        await phone.LoginAsync("admin", "Scolia2026!");
        using var stream = phone.CreateStreamClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await stream.GetAsync("api/direct", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        string? line;
        do { line = await reader.ReadLineAsync(timeout.Token); } while (line is not null && !line.StartsWith("event: ", StringComparison.Ordinal));
        Assert.Equal("event: etat", line);
    }

    public async Task DisposeAsync()
    {
        host.Dispose();
        await host.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await relayServer.StopAsync();
        await relayServer.DisposeAsync();
        secretary.Dispose();
        app.Dispose();
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
