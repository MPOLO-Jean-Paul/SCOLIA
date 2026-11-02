using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;
using Scolia.Bureau.ViewModels;
using Xunit;

namespace Scolia.Bureau.Tests;

/// <summary>Classement de la recherche générale : ce qu'un utilisateur attend en tête, sans formation.</summary>
public class SearchRankingTests
{
    private static ShellViewModel Shell() => new(new ScoliaClient(new Uri("http://127.0.0.1:59999/")), new Preferences());

    [Fact]
    public async Task Un_mot_courant_place_en_tete_la_fonction_dont_le_titre_correspond()
    {
        var search = new SearchViewModel(Shell()) { Query = "police" };
        await search.SearchAsync();
        Assert.Equal("Texte et polices", search.Results[0].Title);
    }

    [Theory]
    [InlineData("acc")]
    [InlineData("mot")]
    [InlineData("son")]
    public async Task Les_fonctions_nommees_passent_avant_celles_trouvees_par_un_mot_cle_cache(string query)
    {
        // « accent » est un mot-clé caché de « Thème et couleurs » : un titre qui contient la saisie passe devant.
        var search = new SearchViewModel(Shell()) { Query = query };
        await search.SearchAsync();
        var named = search.Results.Select(r => PhoneSettingsSearchVm.Matches(query, r.Title)).ToList();
        var firstHidden = named.IndexOf(false);
        Assert.True(firstHidden < 0 || !named.Skip(firstHidden).Contains(true),
            $"Ordre : {string.Join(", ", search.Results.Select(r => r.Title))}");
    }
}
