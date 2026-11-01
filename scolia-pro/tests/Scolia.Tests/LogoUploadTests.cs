using System.Net;
using System.Net.Http.Headers;
using Scolia.Application.Services;

namespace Scolia.Tests;

/// <summary>Logo envoyé par les applications : le type est lu dans le fichier, pas dans l'en-tête de l'envoi.</summary>
public sealed class LogoUploadTests : IDisposable
{
    private readonly ScoliaApp app = new();
    public void Dispose() => app.Dispose();

    // Plus petit PNG valide : un point transparent.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    [Fact]
    public async Task Un_png_sans_type_declare_est_accepte_et_restitue_en_png()
    {
        var admin = await app.InstalledAdmin(demo: false);
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Png), "fichier", "logo.png"); // aucun Content-Type, comme avant le correctif
        var sent = await admin.PutAsync("/api/etablissement/logo", content);
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);

        var back = await admin.GetAsync("/api/etablissement/logo");
        Assert.Equal(HttpStatusCode.OK, back.StatusCode);
        Assert.Equal("image/png", back.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await back.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Un_fichier_qui_n_est_pas_une_image_est_refuse_meme_annonce_comme_png()
    {
        var admin = await app.InstalledAdmin(demo: false);
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent("pas une image"u8.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(part, "fichier", "logo.png");
        var sent = await admin.PutAsync("/api/etablissement/logo", content);
        Assert.False(sent.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, "image/gif")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67 }, null)]
    public void Le_type_est_reconnu_par_les_premiers_octets(byte[] head, string? expected) =>
        Assert.Equal(expected, ImageKind.Detect(head));
}
