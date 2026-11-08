using Scolia.Api.Security;

namespace Scolia.Tests;

/// <summary>Les restrictions sont contrôlées côté API scolaire, pour tous
/// les utilisateurs d'un établissement et tous leurs appareils liés.</summary>
public sealed class LicenseReadOnlyTests
{
    [Theory]
    [InlineData("GET", "/api/apprenants", false)]
    [InlineData("POST", "/api/apprenants", true)]
    [InlineData("PUT", "/api/apprenants/20", true)]
    [InlineData("PATCH", "/api/etablissement", true)]
    [InlineData("DELETE", "/api/inscriptions/20", true)]
    public void Toutes_les_mutations_scolaires_sont_identifiees(string method, string path, bool mutation)
    {
        Assert.Equal(mutation, LicenseMutationPolicy.IsWrite(method));
        if (mutation) Assert.False(LicenseMutationPolicy.AllowedWhenReadOnly(path, method));
    }

    [Theory]
    [InlineData("POST", "/api/session")]
    [InlineData("DELETE", "/api/session")]
    [InlineData("POST", "/api/session/recuperation")]
    [InlineData("POST", "/api/session/mot-de-passe")]
    [InlineData("POST", "/api/jumelage/appareil/verifier")]
    [InlineData("POST", "/api/licence/enregistrer")]
    [InlineData("POST", "/api/licence/actualiser")]
    public void Connexion_et_gestion_de_licence_restent_accessibles_en_lecture_seule(
        string method, string path)
    {
        Assert.True(LicenseMutationPolicy.AllowedWhenReadOnly(path, method));
    }

    [Theory]
    [InlineData("POST", "/api/paiements")]
    [InlineData("POST", "/api/apprenants")]
    [InlineData("POST", "/api/etablissement")]
    [InlineData("POST", "/api/licence-fausse")]
    [InlineData("DELETE", "/api/apprenants/abc")]
    [InlineData("DELETE", "/api/session/recuperation")]
    public void Aucun_endpoint_de_modification_ne_contourne_le_blocage(string method, string path)
    {
        Assert.True(LicenseMutationPolicy.IsWrite(method));
        Assert.False(LicenseMutationPolicy.AllowedWhenReadOnly(path, method));
    }

    [Theory]
    [InlineData("https://licenses.example.org/", true)]
    [InlineData("https://licenses.example.org/path", false)]
    [InlineData("http://licenses.example.org/", false)]
    [InlineData("https://localhost/", false)]
    [InlineData("https://john:secret@licenses.example.org/", false)]
    public void Un_service_de_licences_externe_doît_utiliser_https_sans_secret(
        string address, bool expected)
    {
        Assert.Equal(expected, SchoolLicenseService.IsSecureServiceUrl(address));
    }
}
