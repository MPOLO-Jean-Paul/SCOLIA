using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Pas de divulgation nominative et reprise des notes anonymes entre Android et Windows.</summary>
public sealed class AnonymousTeacherOfflineTests
{
    [Fact]
    public void Note_anonyme_est_mise_en_attente_puis_superposee_au_seul_cours_de_la_feuille()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scolia-prof-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new OfflineStore(new Uri("https://ecole-test.example:5080/"), folder) { User = "prof" };
            var courseId = Guid.NewGuid();
            const string code = "P13W5R";
            var path = $"api/deliberation/feuille?coursId={courseId}&nature=S1&anonyme=true";
            const string sheet = "{\"rows\":[{\"code\":\"P13W5R\",\"value\":null},{\"code\":\"A22B3C\",\"value\":null}]}";
            store.SavePage(path, sheet);
            store.Enqueue(new OutboxItem { User = "prof", Path = "api/deliberation/cotes-par-code",
                Method = "POST", Key = "mark-test", Label = "Encodage d'une note anonyme",
                Body = JsonSerializer.Serialize(new { courseId, kind = "S1", code, value = 17m }, Json.Options) });
            Assert.True(ScoliaClient.Queueable(HttpMethod.Post, "api/deliberation/cotes-par-code"));

            var json = OfflineReplica.Project(path, sheet, store)!;
            using var result = JsonDocument.Parse(json);
            var rows = result.RootElement.GetProperty("rows");
            Assert.Equal(17m, rows[0].GetProperty("value").GetDecimal());
            Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("value").ValueKind);
            Assert.Equal(sheet, OfflineReplica.Project(
                $"api/deliberation/feuille?coursId={Guid.NewGuid()}&nature=S1&anonyme=true", sheet, store));
            Assert.Single(store.Outbox());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Vue_enseignant_impose_la_recherche_de_code_puis_la_note()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln")))
            dir = dir.Parent;
        var basePath = Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(),
            "src", "Scolia.Bureau");
        var view = XDocument.Load(Path.Combine(basePath, "Views", "MarksView.axaml"));
        Assert.Contains(view.Descendants(), x => x.Name.LocalName == "TextBox" &&
            x.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value.Contains("CodeQuery")));
        Assert.Contains(view.Descendants(), x => x.Name.LocalName == "ListBox" &&
            x.Attributes().Any(a => a.Name.LocalName == "ItemsSource" && a.Value.Contains("CodeSuggestions")));
        Assert.Contains(view.Descendants(), x => x.Name.LocalName == "Button" &&
            x.Attributes().Any(a => a.Name.LocalName == "Command" && a.Value.Contains("CheckCode")));
        Assert.Contains(view.Descendants(), x => x.Name.LocalName == "TextBox" &&
            x.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value.Contains("ScoreText")));
        Assert.Contains(view.Descendants(), x => x.Name.LocalName == "Button" &&
            x.Attributes().Any(a => a.Name.LocalName == "Command" && a.Value.Contains("SaveCode")));
    }
}
