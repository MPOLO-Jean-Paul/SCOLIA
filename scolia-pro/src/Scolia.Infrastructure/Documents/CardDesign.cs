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
    public const float IdentityMinPt = 5.2f;
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
    public const float StampLogoRatio = 0.58f;
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
        var widest = Identity(card).Max(line => Ems(line.Label + " : " + line.Value, IdentityTracking));
        var fit = IdentityWidthMm / (widest * PtToMm);
        return Math.Clamp(fit, IdentityMinPt, IdentityMaxPt);
    }

    /// <summary>Texte du pourtour du cachet : « NOM DE L'ÉTABLISSEMENT • VILLE • ».</summary>
    public static string StampRing(SchoolCardHeader header)
    {
        var name = SchoolName(header);
        var city = Clean(header.CityZone)?.ToUpper(Fr);
        var withCity = city is null || name.Contains(city, StringComparison.Ordinal) ? name : name + " • " + city;
        return withCity + " • ";
    }

    /// <summary>Sigle au sommet de l'intérieur du cachet (« UDBL », « ITM »).</summary>
    public static string? StampAcronym(SchoolCardHeader header) => Clean(header.ShortName)?.ToUpper(Fr);

    /// <summary>
    /// Disposition du texte circulaire : corps (pt), et pour chaque caractère son angle en degrés (0 = sommet, sens horaire).
    /// Le texte fait le tour complet quand il est assez long, sinon il est centré au sommet.
    /// </summary>
    public static (float SizePt, IReadOnlyList<(string Glyph, float AngleDeg)> Glyphs) StampRingLayout(string ring)
    {
        const float spacing = 0.10f;
        var radiusMm = StampMm / 2f - StampTextInsetMm;
        var circumference = 2f * MathF.PI * radiusMm;
        var weights = ring.Select(Weight).ToList();
        var ems = weights.Sum() * (1 + spacing);
        // Corps qui fait faire au texte le tour complet, borné pour rester lisible et tenir dans la couronne.
        var sizePt = Math.Clamp(circumference / (ems * PtToMm), 3.3f, 6.0f);
        var unitDeg = sizePt * PtToMm * (1 + spacing) / circumference * 360f;
        var span = ems * unitDeg;
        if (span > 360f) { unitDeg *= 360f / span; span = 360f; }
        // Texte court : centré au sommet ; texte long : il commence au sommet et fait le tour.
        var angle = span >= 359.5f ? 0f : -span / 2f;
        var glyphs = new List<(string Glyph, float AngleDeg)>();
        for (var i = 0; i < ring.Length; i++)
        {
            var advance = weights[i] * (1 + spacing) * unitDeg;
            glyphs.Add((ring[i].ToString(), angle + advance / 2f));
            angle += advance;
        }
        return (sizePt, glyphs);
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
