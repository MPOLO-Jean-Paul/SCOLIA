using System.Text.Json;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Tests;

public sealed class CloudMailboxReceiptTests
{
    [Theory]
    [InlineData("""{"stored":true,"duplicate":false}""", CloudMailboxReceipt.Stored)]
    [InlineData("""{"stored":true,"duplicate":true}""", CloudMailboxReceipt.Stored)]
    [InlineData("""{"stored":false,"outcome":"applied"}""", CloudMailboxReceipt.Applied)]
    [InlineData("""{"stored":false,"outcome":"refused"}""", CloudMailboxReceipt.Refused)]
    public void Seul_un_accuse_explicite_autorise_la_transition_de_l_outbox(
        string json, CloudMailboxReceipt expected)
    {
        using var body = JsonDocument.Parse(json);
        Assert.Equal(expected, CloudMailboxReceiptPolicy.Parse(body.RootElement));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"stored":false}""")]
    [InlineData("""{"stored":"true"}""")]
    [InlineData("""{"outcome":"unknown"}""")]
    [InlineData("""{"outcome":"pending"}""")]
    [InlineData("""{"outcome":42}""")]
    [InlineData("""{"stored":true,"outcome":"inattendue"}""")]
    [InlineData("[]")]
    public void Reponse_non_confirmee_ne_doit_jamais_solde_un_paiement(string json)
    {
        using var body = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(() =>
            CloudMailboxReceiptPolicy.Parse(body.RootElement));
    }

    [Fact]
    public void Issues_connues_ordinaires_ne_soldent_que_les_applied()
    {
        using var body = JsonDocument.Parse(
            """{"outcomes":{"op-1":"applied","op-2":"pending","op-3":"refused","op-4":"unknown"}}""");
        var applied = CloudMailboxReceiptPolicy.AppliedOutcomes(body.RootElement,
            ["op-1", "op-2", "op-3", "op-4", "absent"]);
        Assert.Equal(["op-1"], applied.OrderBy(x => x).ToArray());
    }

    [Theory]
    [InlineData("""{"outcomes":{"op-1":false}}""")]
    [InlineData("""{"outcomes":{"op-1":"autre"}}""")]
    [InlineData("""{"outcomes":[]}""")]
    [InlineData("""{"outcomes":null}""")]
    [InlineData("{}")]
    public void Issues_malformees_sont_refusees_avant_tout_acquittement(string json)
    {
        using var body = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(() =>
            CloudMailboxReceiptPolicy.AppliedOutcomes(body.RootElement, ["op-1"]));
    }

    [Fact]
    public void Un_etat_invalide_apres_un_applied_valide_annule_toute_la_lecture()
    {
        using var body = JsonDocument.Parse(
            """{"outcomes":{"op-1":"applied","op-2":"inconnu_non_autorise"}}""");
        Assert.Throws<InvalidDataException>(() =>
            CloudMailboxReceiptPolicy.AppliedOutcomes(body.RootElement,
                ["op-1", "op-2"]));
    }
}
