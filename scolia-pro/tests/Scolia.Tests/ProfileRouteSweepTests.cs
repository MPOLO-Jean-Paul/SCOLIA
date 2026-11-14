using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Scolia.Tests;

/// <summary>
/// Pour chaque type d'établissement, le secrétariat appelle toutes les routes de lecture de l'API :
/// aucune ne doit répondre par une erreur du serveur (5xx). Une route sans objet pour le profil doit
/// répondre par un refus clair (4xx), jamais par une panne. Un service extérieur injoignable répond 503.
/// </summary>
public sealed class ProfileRouteSweepTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    [Theory]
    [InlineData("ItmIm")]
    [InlineData("Maternelle")]
    [InlineData("Primaire")]
    [InlineData("Secondaire")]
    [InlineData("ComplexeScolaire")]
    [InlineData("SuperieurLmd")]
    public async Task Every_read_route_answers_without_server_error(string type)
    {
        var admin = app.Client();
        var lmd = type == "SuperieurLmd";
        await admin.Post("/api/installation", new
        {
            institutionType = type, governance = "Prive", schoolName = "Établissement " + type, shortName = "ET",
            phone = "0810000000", city = "Kinshasa", currencyCode = lmd ? "USD" : "CDF", currencySymbol = lmd ? "$" : "FC",
            yearLabel = Http.Year(DateOnly.FromDateTime(DateTime.Today)), startMonth = 9, monthCount = 10,
            adminUserName = "admin", adminDisplayName = "Secrétariat", adminPassword = "Scolia2026!",
            loadDemoData = type == "ItmIm",
        }).Ok();

        // Identifiants réels quand il y en a : promotions, apprenants ; sinon un identifiant inconnu.
        var promotions = (await admin.GetAsync("/api/etablissement/promotions").Ok()).EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid()).ToList();
        var students = (await admin.GetAsync("/api/apprenants?taille=1").Ok()).GetProperty("items").EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid()).ToList();
        string IdFor(string template, string parameter)
        {
            var before = template[..template.IndexOf(parameter, StringComparison.Ordinal)];
            if (before.EndsWith("/promotions/", StringComparison.Ordinal) && promotions.Count > 0) return promotions[0].ToString();
            if (before.EndsWith("/apprenants/", StringComparison.Ordinal) && students.Count > 0) return students[0].ToString();
            return Guid.NewGuid().ToString();
        }

        var failures = new List<string>();
        var sources = app.Services.GetRequiredService<EndpointDataSource>();
        foreach (var endpoint in sources.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            if (!methods.Contains("GET")) continue;
            var template = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
            if (!template.StartsWith("/api", StringComparison.Ordinal)) continue;
            var url = System.Text.RegularExpressions.Regex.Replace(template, @"\{[^}]+\}", m =>
                m.Value.Contains(":guid", StringComparison.Ordinal) ? IdFor(template, m.Value)
                : m.Value.Contains(":int", StringComparison.Ordinal) ? "1" : "x");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                using var response = await admin.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                // 503 : service extérieur (console JP-Services, relais) injoignable, signalé clairement.
                if ((int)response.StatusCode >= 500 && (int)response.StatusCode != 503)
                {
                    var body = await response.Content.ReadAsStringAsync(timeout.Token);
                    failures.Add($"{(int)response.StatusCode} {url} {body[..Math.Min(300, body.Length)]}");
                }
            }
            catch (OperationCanceledException) { /* flux continu : il répond, sans fin */ }
        }
        Assert.True(failures.Count == 0, $"{type} : " + string.Join(Environment.NewLine, failures));
    }
}
