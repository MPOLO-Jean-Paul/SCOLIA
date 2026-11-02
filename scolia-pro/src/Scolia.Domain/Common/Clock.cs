namespace Scolia.Domain.Common;

public interface IClock
{
    DateTime UtcNow { get; }
    DateOnly Today { get; }
}
