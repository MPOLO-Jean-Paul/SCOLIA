using Scolia.Application.Abstractions;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Les en-têtes PDF et Word lisent exactement les mêmes valeurs du paramétrage,
/// sans adresse, logo ni organisme codés en dur dans les générateurs.
/// La devise est conservée dans le pied pour réserver une place fixe à
/// l'identité de l'apprenant au milieu de la carte.
/// </summary>
internal static class CardHeaderLayout
{
    public static IReadOnlyList<string> AdministrativeLines(SchoolCardHeader header)
    {
        var lines = new List<string>();
        Add(lines, header.Country);
        Add(lines, header.Ministry);
        Add(lines, Join(header.Direction, header.Division));
        return lines;
    }

    public static string? Location(SchoolCardHeader header) =>
        Join(header.CityZone, header.Address);

    /// <summary>Informations du panneau droit de l'en-tête, toujours issues de l'établissement configuré.</summary>
    public static IReadOnlyList<string> AddressLines(SchoolCardHeader header)
    {
        var lines = new List<string>();
        Add(lines, header.CityZone);
        Add(lines, header.Address);
        return lines;
    }

    /// <summary>Le pied ne doit afficher ni numéro de téléphone ni contacts : devise et année scolaire seulement.</summary>
    public static IReadOnlyList<string> ContactLines(SchoolCardHeader header)
    {
        var lines = new List<string>();
        Add(lines, header.Motto);
        return lines;
    }

    private static void Add(List<string> lines, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add(value.Trim());
    }

    private static string? Join(params string?[] values)
    {
        var text = string.Join(" · ", values.Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase));
        return text.Length == 0 ? null : text;
    }
}
