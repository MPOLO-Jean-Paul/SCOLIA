using Avalonia;
using Avalonia.Media;
using Material.Icons;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Avatars illustrés des fonctionnalités, dessinés en vecteurs (nets à toutes les tailles) : un personnage en
/// buste dans un disque clair, l'objet de la fonctionnalité devant lui (ordinateur, billets, graphique, reçu,
/// liste d'appel…) et une pastille. La chemise prend la teinte de la fonctionnalité.
/// </summary>
public static class Illustrations
{
    private static readonly Dictionary<(MaterialIconKind, Color), IImage> Cache = [];

    private static readonly Color[] Skins = [Color.Parse("#7B4A2E"), Color.Parse("#A0623A"), Color.Parse("#5C3A24"), Color.Parse("#B9784A")];
    private static readonly Color Hair = Color.Parse("#1E1416");
    private static readonly Color Ink = Color.Parse("#2A2340");

    private enum Prop { Laptop, Cash, Chart, Report, Receipt, Checklist, Card, Calendar }

    public static IImage For(MaterialIconKind icon, IBrush tone)
    {
        var color = tone is ISolidColorBrush s ? s.Color : Color.Parse("#2F6FE4");
        lock (Cache)
        {
            if (Cache.TryGetValue((icon, color), out var cached)) return cached;
            var image = Draw(PropOf(icon), color, (int)icon);
            Cache[(icon, color)] = image;
            return image;
        }
    }

    private static Prop PropOf(MaterialIconKind icon) => icon switch
    {
        MaterialIconKind.HandCoin or MaterialIconKind.Cash or MaterialIconKind.CashMultiple or MaterialIconKind.CashRegister => Prop.Cash,
        MaterialIconKind.ChartBar or MaterialIconKind.ChartLine or MaterialIconKind.ViewDashboard or MaterialIconKind.ChartBox => Prop.Chart,
        MaterialIconKind.FileChart or MaterialIconKind.FileDocument or MaterialIconKind.FilePdfBox => Prop.Report,
        MaterialIconKind.ReceiptText or MaterialIconKind.Receipt => Prop.Receipt,
        MaterialIconKind.ClipboardCheck or MaterialIconKind.ClipboardList or MaterialIconKind.ClipboardAccount => Prop.Checklist,
        MaterialIconKind.CardAccountDetails or MaterialIconKind.BadgeAccount => Prop.Card,
        MaterialIconKind.CalendarCheck or MaterialIconKind.CalendarMonth or MaterialIconKind.Calendar => Prop.Calendar,
        _ => Prop.Laptop
    };

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private static SolidColorBrush B(Color c, double opacity = 1) => new(c) { Opacity = opacity };
    private static SolidColorBrush B(string c) => new(Color.Parse(c));
    private static Geometry G(string path) => StreamGeometry.Parse(path);

    private static void Fill(DrawingGroup g, IBrush brush, Geometry geometry) =>
        g.Children.Add(new GeometryDrawing { Brush = brush, Geometry = geometry });

    private static void Stroke(DrawingGroup g, Color color, double thickness, Geometry geometry) =>
        g.Children.Add(new GeometryDrawing
        {
            Geometry = geometry,
            Pen = new Pen(new SolidColorBrush(color), thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round)
        });

    private static EllipseGeometry Circle(double x, double y, double r) => new() { Center = new Point(x, y), RadiusX = r, RadiusY = r };
    private static EllipseGeometry Oval(double x, double y, double rx, double ry) => new() { Center = new Point(x, y), RadiusX = rx, RadiusY = ry };
    private static RectangleGeometry Rect(double x, double y, double w, double h, double r = 0) =>
        new() { Rect = new Rect(x, y, w, h), RadiusX = r, RadiusY = r };

    private static IImage Draw(Prop prop, Color tone, int seed)
    {
        var white = Colors.White;
        var light = Mix(tone, white, 0.9);
        var mid = Mix(tone, white, 0.55);
        var skin = Skins[Math.Abs(seed) % Skins.Length];
        var skinDark = Mix(skin, Colors.Black, 0.18);
        var g = new DrawingGroup();

        // Disque clair, cadre transparent fixe (200 × 200), ombre au sol.
        Fill(g, Brushes.Transparent, Rect(0, 0, 200, 200));
        Fill(g, B(light), Circle(100, 100, 96));
        Fill(g, B(tone, 0.12), Oval(100, 172, 66, 8));

        // Bulle de dialogue en haut à gauche, pastille validée en haut à droite.
        Fill(g, B(white), G("M30,44 h50 a9,9 0 0 1 9,9 v18 a9,9 0 0 1 -9,9 h-34 l-10,10 v-10 h-6 a9,9 0 0 1 -9,-9 v-18 a9,9 0 0 1 9,-9 z"));
        Fill(g, B(mid), Rect(36, 54, 36, 5, 2.5));
        Fill(g, B(Mix(tone, white, 0.75)), Rect(36, 64, 24, 5, 2.5));
        Fill(g, B("#FFB443"), Circle(158, 50, 14));
        Stroke(g, white, 3.4, G("M151,50 l5,5 l9,-10"));

        // Personnage : cheveux, cou, visage, chemise de la teinte de la fonctionnalité.
        Fill(g, B(Hair), Circle(100, 78, 27));
        Fill(g, B(skinDark), Rect(93, 104, 14, 18, 4));
        Fill(g, B(tone), G("M56,178 C56,142 72,120 100,120 C128,120 144,142 144,178 Z"));
        Fill(g, B(Mix(tone, Colors.Black, 0.12)), G("M86,121 Q100,132 114,121 Q108,119 100,119 Q92,119 86,121 Z"));
        Fill(g, B(skin), Circle(100, 89, 23));
        Fill(g, B(Hair), G("M77,84 C77,62 123,62 123,84 C116,74 84,74 77,84 Z"));
        Fill(g, B(Ink), Circle(92, 90, 2.2));
        Fill(g, B(Ink), Circle(108, 90, 2.2));
        Stroke(g, Ink, 2.2, G("M93,98 Q100,104 107,98"));
        Fill(g, B(Color.Parse("#E4877A"), 0.55), Circle(87, 97, 3.2));
        Fill(g, B(Color.Parse("#E4877A"), 0.55), Circle(113, 97, 3.2));

        DrawProp(g, prop, tone, white, skin);
        return new DrawingImage(g);
    }

    private static void DrawProp(DrawingGroup g, Prop prop, Color tone, Color white, Color skin)
    {
        var paper = Color.Parse("#F4F5FA");
        var edge = Color.Parse("#C9CEDA");
        switch (prop)
        {
            case Prop.Cash:
                // Billets et pile de pièces.
                Fill(g, B("#2FA36B"), Rect(58, 136, 78, 40, 6));
                Fill(g, B("#3FBF7F"), Rect(64, 130, 78, 40, 6));
                Fill(g, B("#A7E8C6"), Circle(103, 150, 10));
                Fill(g, B("#2FA36B"), Rect(70, 136, 10, 4, 2));
                Fill(g, B("#2FA36B"), Rect(126, 160, 10, 4, 2));
                foreach (var y in new[] { 168, 160, 152 })
                {
                    Fill(g, B("#E89A1E"), Oval(156, y + 3, 15, 5));
                    Fill(g, B("#FFC24A"), Oval(156, y, 15, 5));
                }
                Hands(g, skin, 70, 160, 136, 158);
                break;
            case Prop.Chart:
                // Tableau de suivi : barres et courbe de retards.
                Fill(g, B(white), Rect(60, 126, 80, 52, 6));
                Stroke(g, edge, 1.5, Rect(60, 126, 80, 52, 6));
                Fill(g, B(Mix(tone, white, 0.4)), Rect(70, 156, 9, 14, 2));
                Fill(g, B(tone), Rect(84, 146, 9, 24, 2));
                Fill(g, B(Mix(tone, white, 0.4)), Rect(98, 151, 9, 19, 2));
                Fill(g, B(tone), Rect(112, 140, 9, 30, 2));
                Stroke(g, Color.Parse("#F0524D"), 2.4, G("M70,142 L86,136 L102,140 L126,132"));
                Hands(g, skin, 62, 172, 138, 172);
                break;
            case Prop.Report:
                // Document à graphique, coin plié.
                Fill(g, B(white), G("M68,122 h50 l14,14 v44 a4,4 0 0 1 -4,4 h-60 a4,4 0 0 1 -4,-4 v-54 a4,4 0 0 1 4,-4 z"));
                Fill(g, B(edge), G("M118,122 v14 h14 z"));
                Fill(g, B(tone), Rect(76, 160, 8, 14, 2));
                Fill(g, B(Mix(tone, white, 0.4)), Rect(88, 152, 8, 22, 2));
                Fill(g, B(tone), Rect(100, 146, 8, 28, 2));
                Fill(g, B(Mix(tone, white, 0.55)), Rect(76, 134, 30, 4, 2));
                Hands(g, skin, 66, 174, 134, 174);
                break;
            case Prop.Receipt:
                // Reçu à bord dentelé, lignes et total.
                Fill(g, B(white), G("M72,120 h56 v58 l-7,-5 l-7,5 l-7,-5 l-7,5 l-7,-5 l-7,5 l-7,-5 l-7,5 z"));
                foreach (var y in new[] { 132, 142, 152 })
                    Fill(g, B(Mix(tone, white, 0.55)), Rect(80, y, 30, 4, 2));
                Fill(g, B(tone), Rect(80, 162, 40, 5, 2.5));
                Fill(g, B(tone), Circle(118, 134, 5));
                Hands(g, skin, 72, 166, 128, 166);
                break;
            case Prop.Checklist:
                // Planchette avec la liste d'appel cochée.
                Fill(g, B(Color.Parse("#8C6A4F")), Rect(66, 122, 68, 56, 6));
                Fill(g, B(white), Rect(71, 130, 58, 44, 3));
                Fill(g, B(edge), Rect(88, 118, 24, 9, 4));
                foreach (var (y, done) in new[] { (140, true), (152, true), (164, false) })
                {
                    Stroke(g, done ? Color.Parse("#22A06B") : edge, 2.4, G(FormattableString.Invariant($"M77,{y} l3,3 l6,-6")));
                    Fill(g, B(Mix(tone, white, done ? 0.45 : 0.7)), Rect(92, y - 3, 30, 4, 2));
                }
                Hands(g, skin, 68, 170, 132, 170);
                break;
            case Prop.Card:
                // Carte d'accès : photo et lignes.
                Fill(g, B(white), Rect(62, 130, 76, 46, 6));
                Stroke(g, edge, 1.5, Rect(62, 130, 76, 46, 6));
                Fill(g, B(tone), Rect(62, 130, 76, 10, 6));
                Fill(g, B(Mix(tone, white, 0.6)), Rect(70, 146, 20, 22, 3));
                Fill(g, B(Mix(tone, white, 0.5)), Rect(96, 148, 34, 4, 2));
                Fill(g, B(Mix(tone, white, 0.7)), Rect(96, 158, 24, 4, 2));
                Hands(g, skin, 64, 166, 136, 166);
                break;
            case Prop.Calendar:
                Fill(g, B(white), Rect(64, 128, 72, 48, 6));
                Fill(g, B(tone), Rect(64, 128, 72, 12, 6));
                for (var row = 0; row < 2; row++)
                    for (var col = 0; col < 4; col++)
                        Fill(g, B(Mix(tone, white, row == 1 && col == 2 ? 0 : 0.7)), Rect(72 + col * 15, 146 + row * 13, 10, 8, 2));
                Hands(g, skin, 66, 170, 134, 170);
                break;
            default:
                // Ordinateur portable ouvert, logo de couleur, posé devant le personnage.
                Fill(g, B(paper), Rect(66, 126, 68, 46, 6));
                Stroke(g, edge, 1.6, Rect(66, 126, 68, 46, 6));
                Fill(g, B(Color.Parse("#F0524D")), Rect(92, 145, 5, 12, 2.5));
                Fill(g, B(tone), Rect(100, 140, 5, 17, 2.5));
                Fill(g, B(tone), Circle(110, 138, 3));
                Fill(g, B(edge), Rect(56, 170, 88, 7, 3.5));
                Hands(g, skin, 62, 172, 138, 172);
                break;
        }
    }

    private static void Hands(DrawingGroup g, Color skin, double lx, double ly, double rx, double ry)
    {
        Fill(g, B(skin), Oval(lx, ly, 9, 6));
        Fill(g, B(skin), Oval(rx, ry, 9, 6));
    }
}
