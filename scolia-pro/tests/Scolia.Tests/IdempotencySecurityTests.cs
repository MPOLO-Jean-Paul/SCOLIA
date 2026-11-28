using System.Net;

namespace Scolia.Tests;

public sealed class IdempotencySecurityTests
{
    [Fact]
    public async Task Identical_request_is_replayed_but_changed_payload_or_route_is_rejected()
    {
        using var app = new ScoliaApp();
        using var admin = await app.InstalledAdmin();
        admin.DefaultRequestHeaders.Add("Idempotency-Key", "compte-test-une-seule-ecriture");

        var request = new
        {
            userName = "agent-idempotence",
            displayName = "Agent de contrôle",
            role = "Comptabilite",
            password = "CodeSolide2026!"
        };

        using var initial = await admin.Post("/api/utilisateurs", request);
        Assert.True(initial.IsSuccessStatusCode, await initial.Content.ReadAsStringAsync());
        var initialBody = await initial.Content.ReadAsStringAsync();

        using var retry = await admin.Post("/api/utilisateurs", request);
        Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        Assert.Equal(initialBody, await retry.Content.ReadAsStringAsync());
        Assert.True(retry.Headers.TryGetValues("Idempotent-Replayed", out var values));
        Assert.Contains("true", values!);

        using var changed = await admin.Post("/api/utilisateurs", new
        {
            userName = "autre-agent",
            displayName = "Une autre personne",
            role = "Comptabilite",
            password = "CodeSolide2026!"
        });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        using var changedRoute = await admin.Post("/api/utilisateurs?source=autre", request);
        Assert.Equal(HttpStatusCode.Conflict, changedRoute.StatusCode);

        var users = await admin.GetAsync("/api/utilisateurs").Ok();
        Assert.Equal(1, users.EnumerateArray().Count(u => u.GetProperty("userName").GetString() == "agent-idempotence"));
        Assert.DoesNotContain(users.EnumerateArray(), u => u.GetProperty("userName").GetString() == "autre-agent");
    }

    [Fact]
    public async Task Persisted_response_is_encrypted_not_a_plaintext_credential_or_json()
    {
        using var app = new ScoliaApp();
        using var admin = await app.InstalledAdmin();
        admin.DefaultRequestHeaders.Add("Idempotency-Key", "test-confidentialite-cache");
        using var result = await admin.Post("/api/utilisateurs", new
        {
            userName = "cache-verification",
            displayName = "Réponse confidentielle",
            role = "Comptabilite",
            password = "SecretTemporaire2026!"
        });
        Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync());

        var files = Directory.GetFiles(Path.Combine(app.DataDirectory, "idempotence"), "*.json");
        var path = Assert.Single(files);
        var contents = await File.ReadAllTextAsync(path);
        Assert.StartsWith("SCOLIA-IDEMPOTENCE-V2:", contents);
        Assert.DoesNotContain("cache-verification", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("Réponse confidentielle", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretTemporaire2026!", contents, StringComparison.Ordinal);
    }
}
