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

/// <summary>Géométries Fluent originales, sans police système ni dépendance réseau, communes à Windows et Android.</summary>
public static class AuthGlyphs
{
    public static Geometry Person { get; } = FluentGlyphs.For(MaterialIconKind.Account);
    public static Geometry Lock { get; } = FluentGlyphs.For(MaterialIconKind.Lock);
    public static Geometry Eye { get; } = FluentGlyphs.For(MaterialIconKind.Eye);
    public static Geometry EyeOff { get; } = FluentGlyphs.For(MaterialIconKind.EyeOffOutline);
    public static Geometry Shield { get; } = FluentGlyphs.For(MaterialIconKind.ShieldCheck);
    public static Geometry Fingerprint { get; } = FluentGlyphs.For(MaterialIconKind.Fingerprint);
    public static Geometry Back { get; } = FluentGlyphs.For(MaterialIconKind.ArrowLeft);
    public static Geometry Expand { get; } = FluentGlyphs.For(MaterialIconKind.ChevronDown);
    public static Geometry Check { get; } = FluentGlyphs.For(MaterialIconKind.Check);
    public static Geometry Info { get; } = FluentGlyphs.For(MaterialIconKind.Information);
    public static Geometry Error { get; } = FluentGlyphs.For(MaterialIconKind.AlertCircle);

    public static readonly IValueConverter EyeConverter = new FuncValueConverter<MaterialIconKind, Geometry>(kind =>
        kind.ToString().Contains("Off", StringComparison.OrdinalIgnoreCase) ? EyeOff : Eye);
}
