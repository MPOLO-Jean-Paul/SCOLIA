using System.Text;
using Microsoft.Data.Sqlite;

namespace Scolia.Bureau.Infrastructure;

/// <summary>Cache et journal SQLite v1. Les valeurs sensibles sont chiffrées avant de parvenir à SQLite/WAL.</summary>
internal sealed class OfflineDatabase
{
    private readonly string connectionString;
    public OfflineDatabase(string root)
    {
        connectionString = new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(root, "hors-ligne.db"), Pooling = false, DefaultTimeout = 15 }.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS state (key TEXT PRIMARY KEY, value BLOB NOT NULL); PRAGMA user_version=1;";
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "PRAGMA synchronous=FULL;";
        command.ExecuteNonQuery();
        return db;
    }

    public string? Read(string key)
    {
        using var db = Open();
        return Read(db, null, key);
    }

    private static string? Read(SqliteConnection db, SqliteTransaction? transaction, string key)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM state WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is byte[] bytes ? Encoding.UTF8.GetString(SecretStore.Unprotect(bytes)) : null;
    }

    public void Write(string key, string value) => Update([key], values => values[key] = value);

    /// <summary>Verrou d'écriture SQLite commun aux instances : lecture et changements font une seule transaction.</summary>
    public void Update(string[] keys, Action<Dictionary<string, string?>> change)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction(deferred: false);
        var values = keys.ToDictionary(k => k, k => Read(db, transaction, k), StringComparer.Ordinal);
        change(values);
        foreach (var (key, value) in values)
        {
            using var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO state(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", SecretStore.Protect(Encoding.UTF8.GetBytes(value ?? string.Empty)));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
}
