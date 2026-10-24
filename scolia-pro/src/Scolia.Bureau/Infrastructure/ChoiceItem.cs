namespace Scolia.Bureau.Infrastructure;

/// <summary>Choix d'une liste déroulante : valeur échangée avec le serveur, libellé affiché.</summary>
public sealed record ChoiceItem(string Value, string Label)
{
    public override string ToString() => Label;
}
