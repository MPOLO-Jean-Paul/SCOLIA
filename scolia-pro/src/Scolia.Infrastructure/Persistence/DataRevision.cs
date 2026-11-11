namespace Scolia.Infrastructure.Persistence;

/// <summary>
/// Numéro de révision des données du serveur. Il augmente à chaque écriture validée, pour que les autres
/// postes du réseau local sachent qu'ils doivent recharger leur écran. Unique pour tout le processus.
/// </summary>
public sealed class DataRevision
{
    private long value;

    public long Value => Interlocked.Read(ref value);

    public void Bump() => Interlocked.Increment(ref value);
}
