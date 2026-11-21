using Scolia.Infrastructure.Persistence;

namespace Scolia.Tests;

/// <summary>
/// Interopérabilité réelle du format ASP.NET Identity V3 avec Cloudflare.
/// Le vecteur est synthétique, obtenu avec une graine publique connue,
/// et ne contient aucun mot de passe ni compte d'un établissement.
/// </summary>
public sealed class PasswordIdentityInteropTests
{
    private const string FictionalPassword = "Scolia-Interop-Fictional-Test-2026!";
    private const string IdentityV3Hash =
        "AQAAAAIAAYagAAAAEAARIjNEVWZ3iJmqu8zd7v9PEBB4MUdff3xFpDXan7LAz4OPm6EQ8dZ4q73OiYwzLQ==";

    [Fact]
    public void AspNetIdentityVerifierAcceptsSameHashAsCloudflarePrototype()
    {
        var passwords = new PasswordService();
        Assert.True(passwords.Verify(IdentityV3Hash, FictionalPassword, out var needsRehash));
        Assert.False(needsRehash);
        Assert.False(passwords.Verify(IdentityV3Hash, "Password-Faux!", out _));
    }

    [Fact]
    public void NewlyCreatedScoliaHashUsesIdentityV3()
    {
        var passwords = new PasswordService();
        var hash = passwords.Hash(FictionalPassword);
        var bytes = Convert.FromBase64String(hash);
        Assert.True(bytes.Length >= 61);
        Assert.Equal(1, bytes[0]);
        Assert.Equal(2U, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(1, 4)));
        Assert.Equal(100_000U, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(5, 4)));
        Assert.True(passwords.Verify(hash, FictionalPassword, out _));
    }
}
