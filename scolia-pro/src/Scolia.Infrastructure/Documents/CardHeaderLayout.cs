using Scolia.Application.Abstractions;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Les en-têtes PDF et Word lisent exactement les mêmes valeurs du paramétrage,
/// sans adresse, logo ni organisme codés en dur dans les générateurs.
/// Les contacts sont conservés dans le pied pour réserver une place fixe à
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

    public static IReadOnlyList<string> ContactLines(SchoolCardHeader header)
    {
        var lines = new List<string>();
        Add(lines, Join(header.Phone, header.Email));
        Add(lines, Join(header.Website, header.Motto));
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
