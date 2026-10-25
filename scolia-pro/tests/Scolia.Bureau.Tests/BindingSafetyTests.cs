using System.Text.RegularExpressions;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>
/// Liaisons dangereuses dans les vues. Un Run et une barre de progression se lient dans les deux sens par défaut :
/// avec un format ou une valeur corrigée, la valeur revient à la source et repart sans fin, jusqu'au plantage.
/// Le tableau de bord a planté ainsi le jour où un apprenant fêtait son anniversaire.
/// </summary>
public partial class BindingSafetyTests
{
    [GeneratedRegex("<(Run|ProgressBar|Slider)\\b[^>]*?(Text|Value)=\"\\{Binding (?<inner>[^\"]*)\\}\"")]
    private static partial Regex RiskyBinding();

    private static string BureauSources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scolia.sln"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("Scolia.sln introuvable."), "src", "Scolia.Bureau");
    }

    [Fact]
    public void Aucun_Run_ni_barre_ne_renvoie_sa_valeur_a_la_source()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(BureauSources(), "*.axaml", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            foreach (Match m in RiskyBinding().Matches(File.ReadAllText(file)))
                if (!m.Groups["inner"].Value.Contains("Mode=OneWay", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)} : {m.Value}");
        Assert.Empty(offenders);
    }
}
