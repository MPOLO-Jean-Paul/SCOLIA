using System.Globalization;
using Avalonia.Data.Converters;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Titres de groupe en capitales (« AUJOURD'HUI »), comme les rubriques de la Configuration.</summary>
public static class TextCase
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    public static readonly IValueConverter Upper = new FuncValueConverter<string?, string>(text => (text ?? string.Empty).ToUpper(French));

    /// <summary>Nature d'une structure ou d'une ligne de maquette LMD, en français (« Faculty » → « Faculté »).</summary>
    public static string LmdKindLabel(string? kind) => kind switch
    {
        "Faculty" => "Faculté",
        "Department" => "Département",
        "Filiere" => "Filière",
        "Domain" => "Domaine",
        "Mention" => "Mention",
        "Pathway" => "Parcours",
        "Speciality" => "Spécialité",
        "TeachingUnit" => "UE",
        "TeachingElement" => "EC",
        "AnnualSubject" => "Cours annuel",
        _ => kind ?? string.Empty,
    };

    /// <summary>
    /// Initiales d'un compte pour son avatar : « jean.paul » → « JP », « Secrétariat Kasongo » → « SK »,
    /// « admin » → « A ». Jamais vide : « ? » pour un compte encore inconnu.
    /// </summary>
    public static string InitialsOf(string? name)
    {
        var parts = (name ?? string.Empty).Split([' ', '.', '_', '-', '@'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p => char.IsLetterOrDigit(p[0])).ToArray();
        if (parts.Length == 0) return "?";
        var first = char.ToUpper(parts[0][0], French);
        return parts.Length == 1 ? first.ToString() : $"{first}{char.ToUpper(parts[1][0], French)}";
    }

    public static readonly IValueConverter Initials = new FuncValueConverter<string?, string>(InitialsOf);

    public static readonly IValueConverter LmdKind = new FuncValueConverter<string?, string>(LmdKindLabel);
}
