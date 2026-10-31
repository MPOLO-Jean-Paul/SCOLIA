using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Scolia.Api;
using Scolia.Application.Abstractions;
using Scolia.Mobile.Server;
using Scolia.Network;

namespace Scolia.Mobile.Tests;

/// <summary>
/// Le téléphone serveur, reproduit sur l'ordinateur : le serveur SCOLIA démarre dans l'application, avec ASP.NET Core pris
/// dans ses propres fichiers comme sur Android. Il installe l'établissement, sert les autres postes, produit ses PDF
/// sans QuestPDF, s'annonce sous le nom du téléphone et garde données et sessions après un redémarrage.
/// </summary>
public sealed class EmbeddedServerTests : IAsyncLifetime
{
    private readonly string data = Path.Combine(Path.GetTempPath(), "scolia-telephone-" + Guid.NewGuid().ToString("N"));
    private readonly int port = FreeTcpPort();
    private readonly int discovery = FreeUdpPort();
    private EmbeddedServer server = null!;

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static int FreeUdpPort()
    {
        using var udp = new UdpClient(0);
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }

    private EmbeddedServer NewServer() => new()
    {
        DataDirectory = data,
        Port = port,
        DiscoveryPort = discovery,
        DeviceName = "Téléphone Samsung A15",
        ConfigureServices = s => s.AddSingleton<IDocumentService, SkiaDocumentService>(),
    };

    public async Task InitializeAsync()
    {
        server = NewServer();
        await server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await server.StopAsync();
        try { Directory.Delete(data, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private HttpClient Client(CookieContainer? cookies = null)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = cookies ?? new CookieContainer() }) { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        client.DefaultRequestHeaders.Add("X-Scolia", "1");
        return client;
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Year()
    {
        var today = DateTime.Now;
        var first = today.Month >= 8 ? today.Year : today.Year - 1;
        return $"{first}-{first + 1}";
    }

    private async Task<HttpClient> Install(CookieContainer cookies)
    {
        var client = Client(cookies);
        await Ok(await client.PostAsJsonAsync("api/installation", new
        {
            schoolName = "École du téléphone", shortName = "ET", motto = "Sans ordinateur", city = "Kinshasa", phone = "0810000000",
            currencyCode = "CDF", currencySymbol = "FC", yearLabel = Year(), startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Direction", adminPassword = "Scolia2026!", loadDemoData = true,
        }));
        return client;
    }

    [Fact]
    public async Task Le_telephone_installe_l_etablissement_et_produit_ses_PDF_sans_QuestPDF()
    {
        using var admin = await Install(new CookieContainer());
        var status = await Ok(await admin.GetAsync("api/installation"));
        Assert.Equal("École du téléphone", status.GetProperty("schoolName").GetString());

        // Un autre poste se connecte au téléphone et lit les apprenants.
        using var other = Client();
        await Ok(await other.PostAsJsonAsync("api/session", new { userName = "admin", password = "Scolia2026!" }));
        var students = await Ok(await other.GetAsync("api/apprenants?taille=5"));
        Assert.True(students.GetProperty("total").GetInt32() > 0);

        // Liste PDF et reçu PDF : dessinés par le téléphone.
        var list = await admin.GetByteArrayAsync("api/rapports/promotions/export?format=pdf");
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(list, 0, 4));
        Assert.DoesNotContain("QuestPDF", Encoding.Latin1.GetString(list), StringComparison.Ordinal);
        var receipts = await Ok(await admin.GetAsync("api/paiements?taille=1"));
        var receiptId = receipts.GetProperty("items")[0].GetProperty("id").GetString();
        var receipt = await admin.GetByteArrayAsync($"api/paiements/{receiptId}/recu.pdf");
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(receipt, 0, 4));
        Assert.DoesNotContain("QuestPDF", Encoding.Latin1.GetString(receipt), StringComparison.Ordinal);

        // Les exports Excel marchent aussi sur le téléphone.
        var excel = await admin.GetByteArrayAsync("api/rapports/apprenants/export?format=xlsx");
        Assert.Equal((byte)'P', excel[0]);
    }

    [Fact]
    public async Task Le_telephone_s_annonce_sur_le_reseau_sous_son_nom()
    {
        using var admin = await Install(new CookieContainer());
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        var (request, nonce) = LanProtocol.NewRequest();
        await udp.SendAsync(request, new IPEndPoint(IPAddress.Loopback, discovery));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reply = await udp.ReceiveAsync(timeout.Token);
        var announcement = LanProtocol.ParseReply(reply.Buffer, nonce);
        Assert.NotNull(announcement);
        Assert.Equal("Téléphone Samsung A15", announcement.Machine);
        Assert.Equal("École du téléphone", announcement.School);
        Assert.Equal(port, announcement.Port);
    }

    [Fact]
    public async Task Apres_un_redemarrage_les_donnees_et_la_session_sont_toujours_la()
    {
        var cookies = new CookieContainer();
        using (var admin = await Install(cookies))
            await Ok(await admin.GetAsync("api/apprenants?taille=1"));

        await server.StopAsync();
        server = NewServer();
        await server.StartAsync();

        // Même cookie : les clés de session sont gardées avec les données du téléphone.
        using var again = Client(cookies);
        var students = await Ok(await again.GetAsync("api/apprenants?taille=1"));
        Assert.True(students.GetProperty("total").GetInt32() > 0);
    }
}
