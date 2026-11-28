/*
Fluent UI System Icons, 24 regular — https://github.com/microsoft/fluentui-system-icons
MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/
using Avalonia.Media;
using Avalonia.Data.Converters;
using Material.Icons;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Icônes Fluent vectorielles des actions de l'accueil, identiques sur tous les DPI.</summary>
public static class HomeGlyphs
{
    public static Geometry Payment { get; } = FluentGlyphs.For(MaterialIconKind.HandCoin);
    public static Geometry PersonAdd { get; } = FluentGlyphs.For(MaterialIconKind.AccountPlus);
    public static Geometry Chart { get; } = FluentGlyphs.For(MaterialIconKind.ChartBar);
    public static Geometry Document { get; } = FluentGlyphs.For(MaterialIconKind.FileDocument);
    public static Geometry Receipt { get; } = FluentGlyphs.For(MaterialIconKind.Receipt);
    public static Geometry Clipboard { get; } = FluentGlyphs.For(MaterialIconKind.ClipboardList);
    public static Geometry Dashboard { get; } = FluentGlyphs.For(MaterialIconKind.ViewDashboardOutline);
    public static Geometry People { get; } = FluentGlyphs.For(MaterialIconKind.AccountGroupOutline);
    public static Geometry Chevron { get; } = FluentGlyphs.For(MaterialIconKind.ChevronRight);
    public static Geometry Apps { get; } = FluentGlyphs.For(MaterialIconKind.DotsGrid);
    public static Geometry School { get; } = FluentGlyphs.For(MaterialIconKind.School);
    public static Geometry Calendar { get; } = FluentGlyphs.For(MaterialIconKind.CalendarRange);
    public static Geometry Home { get; } = FluentGlyphs.For(MaterialIconKind.Home);
    public static Geometry Search { get; } = FluentGlyphs.For(MaterialIconKind.Magnify);
    public static Geometry Dismiss { get; } = FluentGlyphs.For(MaterialIconKind.Close);
    public static Geometry ContactCard { get; } = FluentGlyphs.For(MaterialIconKind.BadgeAccount);

    public static Geometry For(MaterialIconKind kind) => FluentGlyphs.For(kind);
    public static readonly IValueConverter Converter = new FuncValueConverter<MaterialIconKind, Geometry>(For);
}
