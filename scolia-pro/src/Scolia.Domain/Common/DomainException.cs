namespace Scolia.Domain.Common;

/// <summary>Violation d'une règle métier. Le message est destiné à l'utilisateur final.</summary>
public class DomainException : Exception
{
    public string Code { get; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public DomainException(string code, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Code = code;
        Errors = errors;
    }
}

public sealed class NotFoundException(string message) : DomainException("introuvable", message);

public sealed class ConflictException(string message) : DomainException("conflit", message);
