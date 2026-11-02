using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Règles du code d'accès montrées pendant la saisie : mêmes décisions que le serveur.</summary>
public sealed class AccessCodeTests
{
    [Theory]
    [InlineData("482915", null)]
    [InlineData("ma devise ecole", null)]
    [InlineData("Scolia2026x", null)]
    [InlineData("12345", "au moins 6")]
    [InlineData("111111", "trop facile")]
    [InlineData("654321", "trop facile")]
    [InlineData("court", "au moins 8")]
    [InlineData("motdepasse", "trop courant")]
    public void Probleme_du_code(string code, string? expected)
    {
        var problem = AccessCode.Problem(code);
        if (expected is null) Assert.Null(problem);
        else Assert.Contains(expected, problem);
    }

    [Fact]
    public void Les_regles_du_code_pin_se_cochent_pendant_la_saisie()
    {
        Assert.All(AccessCode.Rules("482915", pin: true), r => Assert.True(r.Met));
        Assert.Contains(AccessCode.Rules("4829", pin: true), r => !r.Met);
        Assert.Contains(AccessCode.Rules("48a915", pin: true), r => !r.Met);
    }

    [Fact]
    public void Un_mot_de_passe_fait_de_chiffres_renvoie_vers_le_code_pin()
    {
        Assert.Contains(AccessCode.Rules("48291577", pin: false), r => !r.Met && r.Text.Contains("Code PIN"));
    }
}
