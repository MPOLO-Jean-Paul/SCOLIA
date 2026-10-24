using System.Net;
using System.Net.Http.Json;

namespace Scolia.Bureau.Api;

/// <summary>Client HTTP du serveur SCOLIA : session par cookie et en-tête anti-CSRF, comme l'interface web.</summary>
public sealed class ScoliaClient : IDisposable
{
    private readonly CookieContainer cookies = new();
    private readonly HttpClient http;

    public ScoliaClient(Uri server)
    {
        Server = server;
        var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
        http = new HttpClient(handler) { BaseAddress = server, Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-Scolia", "1");
    }

    public Uri Server { get; }

    public async Task<SessionDto> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/session", new { userName, password }, Json.Options, ct);
        if (!response.IsSuccessStatusCode) throw await ToException(response);
        return (await response.Content.ReadFromJsonAsync<SessionDto>(Json.Options, ct))!;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync("api/session", ct);
        cookies.GetAllCookies().Cast<Cookie>().ToList().ForEach(c => c.Expired = true);
    }

    public async Task<bool> IsRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync("api/sante", ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }
    }

    public async Task<SessionDto?> CurrentSessionAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/session", ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return null;
        if (!response.IsSuccessStatusCode) throw await ToException(response);
        return await response.Content.ReadFromJsonAsync<SessionDto>(Json.Options, ct);
    }

    public Task<DashboardDto> DashboardAsync(CancellationToken ct = default) =>
        GetAsync<DashboardDto>("api/rapports/tableau-de-bord", ct);

    public Task<PagedResult<StudentListItem>> StudentsAsync(string? search, int size, CancellationToken ct = default) =>
        GetAsync<PagedResult<StudentListItem>>($"api/apprenants?q={Uri.EscapeDataString(search ?? string.Empty)}&taille={size}", ct);

    public Task<PagedResult<ReceiptRow>> ReceiptsAsync(int size, CancellationToken ct = default) =>
        GetAsync<PagedResult<ReceiptRow>>($"api/paiements?taille={size}", ct);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode) throw await ToException(response);
        return (await response.Content.ReadFromJsonAsync<T>(Json.Options, ct))!;
    }

    private static async Task<ApiException> ToException(HttpResponseMessage response)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("title", out var title)) return new ApiException(title.GetString() ?? "Erreur");
        }
        catch (System.Text.Json.JsonException) { }
        return new ApiException($"Erreur {(int)response.StatusCode}");
    }

    public void Dispose() => http.Dispose();
}
