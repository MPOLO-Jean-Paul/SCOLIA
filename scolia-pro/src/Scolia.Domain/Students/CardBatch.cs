using Scolia.Domain.Common;

namespace Scolia.Domain.Students;

/// <summary>Historique d'un export de cartes : date, session, promotion, nombre de cartes et de pages, format. Sans liste nominative.</summary>
public sealed class CardBatch : Entity
{
    public string Session { get; set; } = string.Empty;
    public string PromotionLabel { get; set; } = string.Empty;
    public int StudentCount { get; set; }
    public int CardCount { get; set; }
    public int PageCount { get; set; }
    public string Format { get; set; } = "PDF";
    public string FileName { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
}
