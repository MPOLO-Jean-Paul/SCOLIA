namespace Scolia.Domain.Common;

/// <summary>Base de toutes les entités : identifiant ordonné dans le temps et traçabilité.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
    /// <summary>Jeton de concurrence optimiste : incrémenté à chaque modification.</summary>
    public long Version { get; set; }
}
