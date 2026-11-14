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

/// <summary>Action refusée à ce compte (par exemple un enseignant sur un cours qui ne lui est pas attribué).</summary>
public sealed class ForbiddenException(string message) : DomainException("droits", message);
