using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scolia.Application.Abstractions;
using Scolia.Domain.Common;
using Scolia.Domain.Security;

namespace Scolia.Infrastructure.Persistence;

/// <summary>Une écriture à la fois, dans une transaction : cohérence garantie pour les soldes et numérotations.</summary>
public sealed class WriteGate(ScoliaDbContext db, AuditBuffer buffer, DataRevision revision) : IWriteGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly AsyncLocal<bool> Inside = new();

    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (Inside.Value) return await operation(); // Appel imbriqué : déjà protégé.
        await Gate.WaitAsync(cancellationToken);
        Inside.Value = true;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await operation();
                await transaction.CommitAsync(cancellationToken);
                revision.Bump();
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                buffer.Pending.Clear();
                throw;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Ces données ont été modifiées sur un autre poste. Rechargez la page puis recommencez.");
        }
        finally
        {
            Inside.Value = false;
            Gate.Release();
        }
    }
}

/// <summary>Entrées d'audit de la requête, numérotées et chaînées au moment de l'enregistrement.</summary>
public sealed class AuditBuffer
{
    public List<AuditEntry> Pending { get; } = [];
}

public sealed class AuditLog(AuditBuffer buffer, ICurrentUser user, IClock clock) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public void Record(string action, string entityType, string? entityId, string summary, object? data = null)
    {
        buffer.Pending.Add(new AuditEntry
        {
            AtUtc = clock.UtcNow,
            UserName = user.Name,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary.Length > 1000 ? summary[..1000] : summary,
            Data = data is null ? null : JsonSerializer.Serialize(data, Json),
            IpAddress = user.IpAddress,
        });
    }
}

/// <summary>Hachage PBKDF2 (HMAC-SHA512, 100 000 itérations) d'ASP.NET Core Identity.</summary>
public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<AppUser> hasher = new();
    private static readonly AppUser Subject = new();

    public string Hash(string password) => hasher.HashPassword(Subject, password);

    public bool Verify(string hash, string password, out bool needsRehash)
    {
        needsRehash = false;
        try
        {
            var result = hasher.VerifyHashedPassword(Subject, hash, password);
            needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
            return result != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
