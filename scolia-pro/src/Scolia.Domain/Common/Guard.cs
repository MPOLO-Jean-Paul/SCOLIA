namespace Scolia.Domain.Common;

/// <summary>Collecte plusieurs erreurs de validation pour les afficher ensemble dans le formulaire.</summary>
public sealed class ValidationBuilder
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public ValidationBuilder Require(bool condition, string field, string message)
    {
        if (!condition) Add(field, message);
        return this;
    }

    public void Add(string field, string message)
    {
        if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
        list.Add(message);
    }

    public bool IsValid => errors.Count == 0;

    public void ThrowIfInvalid(string message = "Certaines informations sont incorrectes.")
    {
        if (IsValid) return;
        throw new DomainException("validation", message, errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
