using System.Text.RegularExpressions;
using Avalonia.Markup.Xaml;
using Scolia.Domain.School;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Vocabulaire de la session ouverte : « apprenant » dans un ITM/IM, « élève » dans une école,
/// « étudiant » dans une université. Les écrans communs écrivent « apprenant » et passent par
/// <see cref="Adapt"/> ; les adresses de l'API (api/apprenants) ne sont jamais concernées.
/// </summary>
public static partial class Vocabulary
{
    public static InstitutionTerminology Current { get; set; } = InstitutionTerminology.For(null);

    [GeneratedRegex(@"\b([Aa])pprenant(s?)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    /// <summary>Remplace « apprenant(s) », en gardant la majuscule et le pluriel du texte d'origine.</summary>
    public static string Adapt(string? text) => Adapt(text, Current);

    public static string Adapt(string? text, InstitutionTerminology terms)
    {
        if (string.IsNullOrEmpty(text) || terms.Singular == "apprenant") return text ?? string.Empty;
        return Word().Replace(text, m =>
        {
            var plural = m.Groups[2].Value.Length > 0;
            var upper = m.Groups[1].Value == "A";
            return (plural, upper) switch
            {
                (true, true) => terms.PluralTitle,
                (true, false) => terms.Plural,
                (false, true) => terms.SingularTitle,
                _ => terms.Singular,
            };
        });
    }
}

/// <summary>Texte d'un écran commun, dit dans le vocabulaire du profil : <c>{infra:Terms 'Les apprenants cochés'}</c>.</summary>
public sealed class TermsExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => Vocabulary.Adapt(Text);
}
