using System.Buffers.Text;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scolia.Domain.Cloud;
using Scolia.Domain.Common;

namespace Scolia.Api.Cloud;

/// <summary>
/// Automatic SQLite to Cloudflare replication INDEX. The full consistent SQLite
/// snapshot is encrypted and stored in R2 by BackupNowAsync. D1 holds only a
/// second, encrypted append-only manifest pointing to that version. Offline
/// client mutations are submitted to the existing D1 mailbox and replayed to
/// SQLite by DrainAsync. D1 never becomes an independent payment authority.
/// </summary>
public sealed partial class CloudContinuityService
{
    private sealed record MirrorManifest(int Version, string BackupId, string SnapshotSha256,
        string? RevisionEpoch, long Revision, long EncryptedBytes, DateTimeOffset PublishedAt);

    private static string ManifestFrameId(string backupId) =>
        Hex(Encoding.UTF8.GetBytes("scolia-d1-mirror-frame:" + backupId))[..32];

    private static string ManifestStreamId(string backupId) =>
        Hex(Encoding.UTF8.GetBytes("scolia-d1-mirror-stream:" + backupId));

    /// <summary>
    /// Runs on the school's background 2-minute cycle. Two-phase local journal:
    /// persist the AES-GCM payload before the HTTP request, then mark it sent
    /// only after a confirmed D1 acknowledgement. A lost response retries the
    /// *same* ciphertext and id, never generating a conflicting AEAD nonce.
    /// </summary>
    private async Task SyncMirrorAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var current = state;
            if (corrupted || !current.Enabled || !current.MirrorPaired || current.DataKey is null)
                return;
            var key = Convert.FromBase64String(current.DataKey);
            using var client = license.OwnerClient(TimeSpan.FromSeconds(30));

            if (current.LastCloudBackupId is { Length: > 0 } backupId &&
                current.LastCloudBackupSha256 is { Length: 64 } checksum &&
                current.MirroredCloudBackupId != backupId)
            {
                if (current.PendingMirrorBackupId != backupId ||
                    string.IsNullOrWhiteSpace(current.PendingMirrorPayload))
                {
                    var manifest = new MirrorManifest(1, backupId, checksum,
                        current.LastBackupEpoch, current.LastBackupRevision,
                        current.LastBackupBytes, current.LastBackupAt ?? DateTimeOffset.UtcNow);
                    var encrypted = CloudSeal.Seal(JsonSerializer.SerializeToUtf8Bytes(manifest, Json),
                        key, CloudSeal.MirrorContext);
                    var envelope = Base64Url.EncodeToString(encrypted);
                    // Local first: retry after a crash without duplicating the remote frame.
                    Save(state with { PendingMirrorBackupId = backupId, PendingMirrorPayload = envelope });
                    current = state;
                }
                var payload = current.PendingMirrorPayload!;
                var bytes = Base64Url.DecodeFromChars(payload);
                using var response = await client.PostAsJsonAsync("api/v1/server/cloud/mirror/frames", new
                {
                    frame_id = ManifestFrameId(backupId),
                    stream_id = ManifestStreamId(backupId),
                    revision = 1,
                    ciphertext = payload,
                    cipher_sha256 = Hex(bytes),
                }, ct);
                await EnsureSuccessAsync(response, ct);
                var ack = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
                if (!ack.TryGetProperty("stored", out var stored) || stored.ValueKind != JsonValueKind.True)
                    throw new DomainException("nuage", "Accusé D1 invalide : journal local conservé.");
                Save(state with { MirroredCloudBackupId = backupId,
                    PendingMirrorBackupId = null, PendingMirrorPayload = null });
            }

            // Cloud to local: only authenticate and verify the encrypted *index*.
            // Existing mailbox replay to actual SQLite remains in DrainAsync.
            // Never overwrite the live school database with a snapshot.
            var cursor = state.LastRemoteMirrorCursor;
            using var listing = await client.GetAsync(
                $"api/v1/server/cloud/mirror/frames?after={cursor}&limit=20", ct);
            await EnsureSuccessAsync(listing, ct);
            var page = await listing.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            if (!page.TryGetProperty("frames", out var frames) || frames.ValueKind != JsonValueKind.Array)
                throw new DomainException("nuage", "Liste de révisions D1 invalide.");
            foreach (var row in frames.EnumerateArray())
            {
                var seq = row.GetProperty("seq").GetInt64();
                var encoded = row.GetProperty("ciphertext").GetString() ?? "";
                var hash = row.GetProperty("cipher_sha256").GetString() ?? "";
                if (seq <= cursor || seq < 1 || encoded.Length > 33000)
                    throw new DomainException("nuage", "Curseur D1 non monotone.");
                byte[] ciphertext;
                try { ciphertext = Base64Url.DecodeFromChars(encoded); }
                catch (FormatException) { throw new DomainException("nuage", "Trame D1 mal encodée."); }
                if (!string.Equals(Hex(ciphertext), hash, StringComparison.Ordinal))
                    throw new DomainException("nuage", "Trame D1 altérée : reprise suspendue.");
                var clear = CloudSeal.Open(ciphertext, key, CloudSeal.MirrorContext);
                MirrorManifest? manifest;
                try { manifest = JsonSerializer.Deserialize<MirrorManifest>(clear, Json); }
                finally { CryptographicOperations.ZeroMemory(clear); }
                if (manifest is not { Version: 1, BackupId.Length: > 0, SnapshotSha256.Length: 64 } ||
                    !Guid.TryParse(manifest.BackupId, out _) || manifest.Revision < 0 ||
                    manifest.EncryptedBytes < 1 ||
                    !string.Equals(row.GetProperty("frame_id").GetString(),
                        ManifestFrameId(manifest.BackupId), StringComparison.Ordinal))
                    throw new DomainException("nuage", "Version de sauvegarde D1 invalide.");
                cursor = seq;
            }
            if (cursor != state.LastRemoteMirrorCursor)
                Save(state with { LastRemoteMirrorCursor = cursor });
        }
        finally { gate.Release(); }
    }
}
