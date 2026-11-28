using System.Net;
using Microsoft.AspNetCore.Http;
using Scolia.Api.Endpoints;

namespace Scolia.Tests;

/// <summary>
/// Même avec les droits « paramètres », un client ou relais distant ne peut
/// ni enregistrer une école ni imposer une configuration de relais.
/// </summary>
public sealed class LicenseAdministrationLocalTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.1.25", false)]
    [InlineData("10.0.0.12", false)]
    [InlineData("2001:db8::9", false)]
    public void Administration_reservee_a_la_boucle_locale(string remote, bool expected)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        Assert.Equal(expected, LicenseEndpoints.IsServerLocal(http));
    }

    [Theory]
    [InlineData("X-Scolia-Relay-Connection", "1")]
    [InlineData("X-Forwarded-For", "192.168.1.25")]
    [InlineData("Forwarded", "for=192.168.1.25")]
    public void Relais_et_proxys_ne_peuvent_pas_se_faire_passer_pour_l_administrateur_local(
        string header, string value)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        http.Request.Headers[header] = value;
        Assert.False(LicenseEndpoints.IsServerLocal(http));
    }

    [Fact]
    public void Une_adresse_non_renseignee_ne_donne_pas_l_acces_proprietaire()
    {
        var http = new DefaultHttpContext();
        Assert.False(LicenseEndpoints.IsServerLocal(http));
    }
}
