using System.Globalization;
using Avalonia.Data.Converters;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Vrai quand une liste n'a aucun élément : sert à afficher le message d'état vide sous un tableau.</summary>
public sealed class IsEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is int count && count == 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
