using System.Globalization;
using Scolia.Application.Abstractions;
using Scolia.Domain.Students;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Dessin commun des cartes d'accès (PDF de l'ordinateur, PDF du téléphone, Word) : mêmes mesures, mêmes couleurs,
/// mêmes textes. Modèle « porte-badge universitaire » : en-tête centré (logo, nom de l'établissement en capitales
/// à empattements, service, filet, contacts), « Session AAAA » en grand, souligné, matricule, noms et promotion
/// en capitales espacées, et le cachet rond de l'établissement en bas à droite, marqué « PAYÉ » quand l'apprenant
/// est en ordre. Toutes les mentions viennent de l'établissement configuré.
/// Les mesures sont en millimètres depuis le coin supérieur gauche de la carte (85,60 × 53,98 mm).
/// </summary>
public static class CardDesign
{
    // ---------- Couleurs ----------
    public const string Paper = "#FFFFFF";
    public const string Frame = "#C9CFD8";
    public const string HeaderInk = "#2B2F36";
    public const string HeaderMuted = "#4A5058";
    public const string SessionInk = "#6A6FB8";
    public const string IdentityInk = "#4B525C";
    /// <summary>Encre du cachet : bleu tampon, légèrement transparent pour laisser lire ce qu'il recouvre.</summary>
    public const string StampInk = "#2C66AE";
    public const byte StampAlpha = 0xD9;
    public const string PaidInk = "#3B8AD3";
    public const byte PaidAlpha = 0xE6;

    // ---------- Polices ----------
    public const string SerifFamily = "Liberation Serif";
    public const string SansFamily = "Montserrat";

    // ---------- En-tête ----------
    public const float LogoMm = 11f;
    public const float HeaderGapMm = 1.8f;
    public const float HeaderTextMm = 41f;
    public const float HeaderTopMm = 2.9f;
    public const float HeaderHeightMm = 13.2f;
    public static float HeaderLeftMm => (StudentCardRules.CardWidthMm - (LogoMm + HeaderGapMm + HeaderTextMm)) / 2f;
    public static float HeaderTextLeftMm => HeaderLeftMm + LogoMm + HeaderGapMm;
    public const float DepartmentPt = 3.5f;
    public const float ContactPt = 3.1f;

    // ---------- QR de contrôle (coin supérieur droit, discret) ----------
    public const float QrMm = 9.4f;
    public const float QrMarginMm = 2.1f;
    public static float QrLeftMm => StudentCardRules.CardWidthMm - QrMarginMm - QrMm;

    // ---------- Session ----------
    public const float SessionTopMm = 17.6f;
    public const float SessionPt = 15.5f;

    // ---------- Identité ----------
    public const float IdentityLeftMm = 9.5f;
    public const float IdentityTopMm = 28.3f;
    public const float IdentityStepMm = 4.75f;
    public const float IdentityMaxPt = 7.3f;
    public const float IdentityMinPt = 4.2f;
    /// <summary>Hauteur d'une ligne d'identité : une seule ligne y tient, un texte trop long est réduit, jamais renvoyé à la ligne.</summary>
    public const float IdentityLineMm = 3.4f;
    public const float IdentityTracking = 0.06f;
    public static float IdentityWidthMm => StudentCardRules.CardWidthMm - IdentityLeftMm - 2.2f;

    // ---------- Cachet ----------
    public const float StampMm = 27.4f;
    public const float StampCenterXMm = 69.4f;
    public const float StampCenterYMm = 39.2f;
    public static float StampLeftMm => StampCenterXMm - StampMm / 2f;
    public static float StampTopMm => StampCenterYMm - StampMm / 2f;
    /// <summary>Cercles du cachet, depuis le bord : double filet extérieur, filet intérieur.</summary>
    public const float StampOuterInsetMm = 0.45f;
    public const float StampSecondInsetMm = 1.05f;
    public const float StampInnerInsetMm = 4.25f;
    /// <summary>Rayon de la ligne de base du texte circulaire, depuis le bord.</summary>
    public const float StampTextInsetMm = 3.45f;
    public const float StampLogoMm = 8.4f;
    /// <summary>Décalages verticaux depuis le centre du cachet : logo un peu au-dessus, « PAYÉ » en dessous.</summary>
    public const float StampLogoOffsetMm = -1.5f;
    public const float PaidOffsetMm = 4.5f;
    public const float StampAcronymPt = 4.3f;
    public const float PaidPt = 17.5f;
    public const string PaidMark = "PAYÉ";

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // ---------- Textes ----------

    /// <summary>Nom de l'établissement, en capitales : la première ligne de l'en-tête.</summary>
    public static string SchoolName(SchoolCardHeader header) => header.Institute.Trim().ToUpper(Fr);

    /// <summary>Taille du nom : deux lignes au plus dans la largeur de l'en-tête, plus petit quand il est long.</summary>
    public static float SchoolNamePt(string name) => name.Length switch
    {
        <= 46 => 6.7f,
        <= 60 => 5.9f,
        <= 78 => 5.1f,
        _ => 4.4f,
    };

    /// <summary>Service ou tutelle, sous le nom : direction et division, sinon le ministère.</summary>
    public static string? Department(SchoolCardHeader header)
    {
        var service = Join(" · ", header.Direction, header.Division);
        return (service ?? Clean(header.Ministry))?.ToUpper(Fr);
    }

    /// <summary>Contacts sous le filet : téléphone, courriel et site, puis adresse, ville et pays.</summary>
    public static IReadOnlyList<string> Contacts(SchoolCardHeader header)
    {
        var lines = new List<string>();
        var phone = Clean(header.Phone);
        if (Join("  ·  ", phone is null ? null : "Tél. " + phone, header.Email, header.Website) is { } first) lines.Add(first);
        if (Join(", ", header.Address, header.CityZone, header.Country) is { } second) lines.Add(second);
        return lines;
    }

    /// <summary>Titre de la carte : « Session 2026 ».</summary>
    public static string SessionTitle(string session) => "Session " + StudentCardRules.SessionYear(session);

    /// <summary>Noms comme sur la carte modèle : nom et post-nom en capitales, prénom tel qu'écrit (« MUKENDI TSHIDIBI Danny »).</summary>
    public static string DisplayName(CardItem card)
    {
        var family = string.Join(' ', new[] { card.LastName, card.MiddleName }.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim().ToUpper(Fr)));
        var first = card.FirstName.Trim();
        if (first.Length > 0 && first == first.ToUpper(Fr))
            first = Fr.TextInfo.ToTitleCase(first.ToLower(Fr));
        return string.Join(' ', new[] { family, first }.Where(v => v.Length > 0));
    }

    /// <summary>Les trois lignes d'identité, libellé et valeur.</summary>
    public static IReadOnlyList<(string Label, string Value)> Identity(CardItem card) =>
    [
        ("MATRICULE", card.Matricule.Trim().ToUpper(Fr)),
        ("NOMS", DisplayName(card)),
        ("PROMOTION", card.Classe.Trim().ToUpper(Fr)),
    ];

    /// <summary>
    /// Taille commune des trois lignes d'identité : la plus longue tient dans la largeur, les trois restent de même corps,
    /// comme sur la carte modèle.
    /// </summary>
    public static float IdentityPt(CardItem card)
    {
        var widest = Identity(card).Max(line => Ems(line.Label + " : " + line.Value, IdentityTracking)) * 1.08f;
        var fit = IdentityWidthMm / (widest * PtToMm);
        return Math.Clamp(fit, IdentityMinPt, IdentityMaxPt);
    }

    /// <summary>Haut du pourtour du cachet : le nom de l'établissement.</summary>
    public static string StampTop(SchoolCardHeader header) => SchoolName(header);

    /// <summary>Bas du pourtour du cachet, lisible à l'endroit : la ville (« LUBUMBASHI »), si elle n'est pas déjà dans le nom.</summary>
    public static string? StampBottom(SchoolCardHeader header)
    {
        var city = Clean(header.CityZone)?.ToUpper(Fr);
        return city is null || SchoolName(header).Contains(city, StringComparison.Ordinal) ? null : city;
    }

    /// <summary>Sigle au sommet de l'intérieur du cachet (« UDBL », « ITM »).</summary>
    public static string? StampAcronym(SchoolCardHeader header) => Clean(header.ShortName)?.ToUpper(Fr);

    /// <summary>Un caractère du pourtour : angle en degrés depuis le sommet (sens horaire) ; en bas, il est retourné pour se lire à l'endroit.</summary>
    public readonly record struct StampGlyph(string Glyph, float AngleDeg, bool Bottom);

    /// <summary>
    /// Pourtour du cachet comme un tampon officiel : le nom en arc au sommet, la ville en arc en bas lisible à l'endroit,
    /// séparés de chaque côté par un point. Rend le corps (pt) et la place de chaque caractère.
    /// </summary>
    public static (float SizePt, IReadOnlyList<StampGlyph> Glyphs) StampRingLayout(SchoolCardHeader header)
    {
        const float spacing = 0.12f;
        var top = StampTop(header);
        var bottom = StampBottom(header);
        var radiusMm = StampMm / 2f - StampTextInsetMm;
        var degPerMm = 360f / (2f * MathF.PI * radiusMm);
        float EmsOf(string text) => text.Sum(Weight) * (1 + spacing);
        var topMax = bottom is null ? 310f : 232f;
        const float bottomMax = 96f;
        var size = topMax / degPerMm / (EmsOf(top) * PtToMm);
        if (bottom is not null) size = Math.Min(size, bottomMax / degPerMm / (EmsOf(bottom) * PtToMm));
        size = Math.Clamp(size, 3.0f, 5.8f);
        var unit = size * PtToMm * degPerMm * (1 + spacing);

        var glyphs = new List<StampGlyph>();
        var topSpan = top.Sum(Weight) * unit;
        var topScale = topSpan > topMax ? topMax / topSpan : 1f;
        var angle = -Math.Min(topSpan, topMax) / 2f;
        foreach (var c in top)
        {
            var advance = Weight(c) * unit * topScale;
            glyphs.Add(new StampGlyph(c.ToString(), angle + advance / 2f, false));
            angle += advance;
        }
        var topEnd = angle;
        if (bottom is not null)
        {
            var bottomSpan = bottom.Sum(Weight) * unit;
            var bottomScale = bottomSpan > bottomMax ? bottomMax / bottomSpan : 1f;
            bottomSpan *= bottomScale;
            // De gauche à droite en bas, l'angle décroît (le bas-gauche est vers 200°, le bas-droit vers 160°).
            var at = 180f + bottomSpan / 2f;
            foreach (var c in bottom)
            {
                var advance = Weight(c) * unit * bottomScale;
                glyphs.Add(new StampGlyph(c.ToString(), at - advance / 2f, true));
                at -= advance;
            }
            var bottomStart = 180f - bottomSpan / 2f;
            var side = (topEnd + bottomStart) / 2f;
            glyphs.Add(new StampGlyph("•", side, false));
            glyphs.Add(new StampGlyph("•", -side, false));
        }
        return (size, glyphs);
    }

    /// <summary>Un point typographique en millimètres.</summary>
    public const float PtToMm = 25.4f / 72f;

    /// <summary>Largeur approchée d'un texte en cadratins (Montserrat), espacement compris.</summary>
    public static float Ems(string text, float tracking = 0) => text.Sum(c => Weight(c) + tracking);

    private static float Weight(char c) => c switch
    {
        ' ' => 0.28f,
        '•' or '·' or '.' or ',' or ':' or '\'' => 0.32f,
        'I' or 'l' or 'i' or 'j' or '1' => 0.34f,
        'M' or 'W' or 'm' or 'w' => 0.88f,
        _ when char.IsUpper(c) => 0.70f,
        _ when char.IsDigit(c) => 0.64f,
        _ => 0.58f,
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Join(string separator, params string?[] values)
    {
        var parts = values.Select(Clean).Where(v => v is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return parts.Count == 0 ? null : string.Join(separator, parts);
    }

    /// <summary>Couleur avec transparence au format #AARRGGBB.</summary>
    public static string WithAlpha(string color, byte alpha) => $"#{alpha:X2}{color.TrimStart('#')}";
}
