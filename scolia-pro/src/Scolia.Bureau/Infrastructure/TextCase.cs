using System.Globalization;
using Avalonia.Data.Converters;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Titres de groupe en capitales (« AUJOURD'HUI »), comme les rubriques de la Configuration.</summary>
public static class TextCase
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    public static readonly IValueConverter Upper = new FuncValueConverter<string?, string>(text => (text ?? string.Empty).ToUpper(French));
}
