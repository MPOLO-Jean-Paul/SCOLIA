using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Tests;

public sealed class CentralPairingClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => send(request, ct);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task Jumelage_natif_demande_mot_de_passe_signe_avec_ECDSA_et_ne_cree_pas_de_session()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-pair-native-" + Guid.NewGuid().ToString("N"));
        var invitation = Guid.NewGuid();
        var device = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        const string user = "ENSEIGNANT";
        const string password = "MotDePasse-Valide-2026";
        string? proofThumbprint = null;
        var seen = 0;

        try
        {
            using var gateway = new CentralPairingClient(new Handler(async (request, ct) =>
            {
                seen++;
                Assert.Equal("staging.example", request.RequestUri!.Host);
                Assert.Equal(Uri.UriSchemeHttps, request.RequestUri!.Scheme);
                var body = await request.Content!.ReadAsStringAsync(ct);
                using var parsed = JsonDocument.Parse(body);
                if (request.RequestUri.AbsolutePath == "/v1/identity/pairing-proof")
                {
                    Assert.Equal(1, seen);
                    Assert.Equal(user, parsed.RootElement.GetProperty("user_name").GetString());
                    Assert.Equal(password, parsed.RootElement.GetProperty("password").GetString());
                    Assert.Equal(invitation.ToString("D"), parsed.RootElement.GetProperty("invitation_id").GetString());
                    proofThumbprint = parsed.RootElement.GetProperty("device_key_thumbprint").GetString();
                    return Json(HttpStatusCode.OK,
                        """{"pairing_proof":"header.payload.signature-long-enough","session_issued":false}""");
                }
                Assert.Equal("/v1/pair/accept", request.RequestUri.AbsolutePath);
                Assert.Equal(2, seen);
                Assert.Equal("Bearer header.payload.signature-long-enough",
                    request.Headers.GetValues("X-Scolia-Account-Proof").Single());
                Assert.False(body.Contains(password, StringComparison.Ordinal));
                var root = parsed.RootElement;
                Assert.Equal(token, root.GetProperty("token").GetString());
                Assert.Equal(device, root.GetProperty("device_id").GetString());
                var pub = root.GetProperty("public_key_jwk");
                var crv = pub.GetProperty("crv").GetString();
                var kty = pub.GetProperty("kty").GetString();
                var x = pub.GetProperty("x").GetString()!;
                var y = pub.GetProperty("y").GetString()!;
                Assert.Equal("P-256", crv);
                Assert.Equal("EC", kty);
                var canonical = JsonSerializer.Serialize(new { crv, kty, x, y });
                var fingerprint = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_');
                Assert.Equal(proofThumbprint, fingerprint);

                static byte[] Decode(string text) => Convert.FromBase64String(
                    text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
                using var pubKey = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = Decode(x), Y = Decode(y) },
                });
                var challenge = Encoding.UTF8.GetBytes(
                    $"SCOLIA-PAIR-V1\n{invitation:D}\n{device}\n{token}");
                Assert.True(pubKey.VerifyData(challenge, Decode(root.GetProperty("device_signature").GetString()!),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

                return Json(HttpStatusCode.Created,
                    "{\"device_id\":\"" + device + "\",\"school_id\":\"school-a\",\"approved\":true,\"session_issued\":false}");
            }), temp);

            var paired = await gateway.PairAsync(new Uri("https://staging.example/"),
                invitation, token, user, password, device);
            Assert.True(paired.Approved);
            Assert.Equal("school-a", paired.SchoolId);
            Assert.False(paired.SessionIssued);
            Assert.Equal(2, seen);
            Assert.Equal(new Uri("https://staging.example/"),
                gateway.ReadActiveOrigin(user, device));
            Assert.Null(gateway.ReadActiveOrigin("AUTRE_COMPTE", device));
            Assert.Throws<InvalidOperationException>(() =>
                gateway.SelectApprovedOrigin(new Uri("https://production.example/"), user, device));
            Assert.Equal(new Uri("https://staging.example/"),
                gateway.ReadActiveOrigin(user, device));
            Assert.Single(Directory.GetFiles(temp, "*.enc"));
            var encryptedOrigin = File.ReadAllBytes(
                Directory.GetFiles(temp, "*.enc")[0]);
            Assert.DoesNotContain("staging.example",
                Convert.ToBase64String(encryptedOrigin), StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(temp, "*.key"));
            var encrypted = File.ReadAllBytes(Directory.GetFiles(temp, "*.key")[0]);
            Assert.False(Encoding.UTF8.GetString(encrypted).Contains(password, StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    [Fact]
    public async Task Refus_identite_ne_transmet_pas_le_QR_au_second_endpoint()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-pair-reject-" + Guid.NewGuid().ToString("N"));
        var sent = 0;
        try
        {
            using var gateway = new CentralPairingClient(new Handler((request, _) =>
            {
                sent++;
                Assert.Equal("/v1/identity/pairing-proof", request.RequestUri!.AbsolutePath);
                return Task.FromResult(Json(HttpStatusCode.Unauthorized,
                    """{"error":"invalid_credentials_or_invitation"}"""));
            }), temp);
            var ex = await Assert.ThrowsAsync<CentralPairingException>(() => gateway.PairAsync(
                new Uri("https://staging.example/"), Guid.NewGuid(), new string('A', 43),
                "enseignant", "incorrect", "android-01"));
            Assert.Equal(401, ex.Status);
            Assert.Equal(1, sent);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    [Fact]
    public async Task Appareil_appaire_ouvre_une_session_centrale_sans_renvoyer_le_mot_de_passe()
    {
        var temp = Path.Combine(Path.GetTempPath(), "scolia-central-session-" + Guid.NewGuid().ToString("N"));
        var device = Guid.NewGuid().ToString("N");
        var invitation = Guid.NewGuid();
        const string jwt = "header-central.payload-central.signature-central-123456";
        const string user = "ENSEIGNANT";
        const string secret = "mot-de-passe-uniquement-pendant-jumelage";
        string? jwkJson = null;
        var calls = 0;

        static byte[] From64(string input) => Convert.FromBase64String(
            input.Replace('-', '+').Replace('_', '/') + new string('=', (4 - input.Length % 4) % 4));

        try
        {
            using var gateway = new CentralPairingClient(new Handler(async (request, ct) =>
            {
                calls++;
                var text = await request.Content!.ReadAsStringAsync(ct);
                using var parsed = JsonDocument.Parse(text);
                if (request.RequestUri!.AbsolutePath == "/v1/identity/pairing-proof")
                {
                    Assert.Equal(secret, parsed.RootElement.GetProperty("password").GetString());
                    return Json(HttpStatusCode.OK,
                        """{"pairing_proof":"header.payload.signature-long-enough","session_issued":false}""");
                }
                if (request.RequestUri.AbsolutePath == "/v1/pair/accept")
                {
                    jwkJson = parsed.RootElement.GetProperty("public_key_jwk").GetRawText();
                    return Json(HttpStatusCode.Created,
                        "{\"device_id\":\"" + device + "\",\"school_id\":\"school-a\"," +
                        "\"approved\":true,\"session_issued\":false}");
                }

                Assert.Equal("/v1/identity/session", request.RequestUri.AbsolutePath);
                Assert.NotNull(jwkJson);
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
                var body = parsed.RootElement;
                Assert.Equal("school-a", body.GetProperty("school_id").GetString());
                Assert.Equal(device, body.GetProperty("device_id").GetString());
                var nonce = body.GetProperty("nonce").GetString()!;
                var issuedAt = body.GetProperty("issued_at").GetInt64();
                using var keyDoc = JsonDocument.Parse(jwkJson!);
                var publicPart = keyDoc.RootElement;
                using var publicKey = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint
                    {
                        X = From64(publicPart.GetProperty("x").GetString()!),
                        Y = From64(publicPart.GetProperty("y").GetString()!),
                    },
                });
                var challenge = Encoding.UTF8.GetBytes(
                    $"SCOLIA-SESSION-V1\nschool-a\n{device}\n{nonce}\n{issuedAt}");
                Assert.True(publicKey.VerifyData(challenge,
                    From64(body.GetProperty("signature").GetString()!),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
                return Json(HttpStatusCode.OK,
                    "{\"access_token\":\"" + jwt + "\",\"expires_in\":900," +
                    "\"school_id\":\"school-a\",\"device_id\":\"" + device + "\"}");
            }), temp);

            var paired = await gateway.PairAsync(new Uri("https://staging.example/"),
                invitation, new string('A', 43), user, secret, device);
            Assert.Equal("school-a", paired.SchoolId);
            Assert.Equal("school-a", gateway.ReadSavedSchoolId(
                new Uri("https://staging.example/"), user, device));
            var session = await gateway.OpenSavedSessionAsync(
                new Uri("https://staging.example/"), user, device);
            Assert.Equal(jwt, session.AccessToken);
            Assert.Equal("school-a", session.SchoolId);
            Assert.Equal(device, session.DeviceId);
            Assert.Equal(3, calls);
            Assert.Equal(new Uri("https://staging.example/"),
                gateway.ReadActiveOrigin(user, device));
            gateway.DisableActiveOrigin(user, device);
            Assert.Null(gateway.ReadActiveOrigin(user, device));
            // Désactiver la synchronisation ne détruit pas le jumelage.
            Assert.Equal("school-a", gateway.ReadSavedSchoolId(
                new Uri("https://staging.example/"), user, device));
            gateway.SelectApprovedOrigin(new Uri("https://staging.example/"), user, device);
            Assert.NotNull(gateway.ReadActiveOrigin(user, device));
            Assert.Single(Directory.GetFiles(temp, "*.key"));
            Assert.Single(Directory.GetFiles(temp, "*.meta"));
            var encryptedMeta = File.ReadAllBytes(Directory.GetFiles(temp, "*.meta")[0]);
            Assert.DoesNotContain("school-a", Convert.ToBase64String(encryptedMeta),
                StringComparison.Ordinal);
            Assert.DoesNotContain(jwt, File.ReadAllText(Directory.GetFiles(temp, "*.key")[0]),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    [Fact]
    public async Task Cle_non_jumelee_ne_peut_demander_une_session()
    {
        var calls = 0;
        var temp = Path.Combine(Path.GetTempPath(), "scolia-central-unpaired-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var gateway = new CentralPairingClient(new Handler((_, _) =>
            {
                calls++;
                throw new InvalidOperationException("Aucun appel réseau attendu.");
            }), temp);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateway.OpenSessionAsync(new Uri("https://staging.example/"), "school-a",
                    "ENSEIGNANT", "phone-a"));
            Assert.Equal(0, calls);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    [Fact]
    public async Task Destinations_non_HTTPS_et_routes_dangereuses_sont_refusees_avant_la_requete()
    {
        var sent = 0;
        using var gateway = new CentralPairingClient(new Handler((_, _) =>
        {
            sent++;
            throw new InvalidOperationException("Le secret ne doit jamais partir sur ce domaine.");
        }));
        foreach (var url in new[]
        {
            "http://staging.example/", "https://user:pass@staging.example/",
            "https://staging.example/other", "https://staging.example/?redirect=https://other.example",
        })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => gateway.PairAsync(
                new Uri(url), Guid.NewGuid(), new string('A', 43),
                "enseignant", "secret", "android-01"));
        }
        Assert.Equal(0, sent);
    }
}
