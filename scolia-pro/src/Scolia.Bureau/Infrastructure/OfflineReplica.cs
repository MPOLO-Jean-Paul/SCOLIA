using System.Text.Json;
using System.Text.Json.Nodes;
using Scolia.Bureau.Api;

namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Read-your-writes dans une réplique locale : la copie du serveur reste intacte,
/// les opérations non confirmées sont superposées à l'affichage dans leur ordre.
/// Un refus ou un abandon retire automatiquement sa projection ; aucune opération
/// ne devient une écriture serveur sans acquittement.
/// </summary>
public static class OfflineReplica
{
    private static readonly string[] StudentFields =
    [
        "lastName", "middleName", "firstName", "gender", "birthDate", "birthPlace",
        "nationality", "address", "phone", "email", "guardianName", "guardianRelation",
        "guardianPhone", "guardianPhone2", "notes",
    ];

    /// <summary>Retourne la dernière vue de travail à partir d'un cliché et de l'outbox.</summary>
    public static string? Project(string path, string? snapshot, OfflineStore store)
    {
        // Un compte reste utilisable pendant la coupure : déduire de son
        // cliché les échéances déjà encaissées localement, y compris partiellement.
        if (path.StartsWith("api/inscriptions/", StringComparison.Ordinal) &&
            path.EndsWith("/compte", StringComparison.Ordinal) && snapshot is not null)
        {
            if (JsonSerializer.Deserialize<AccountDto>(snapshot, Json.Options) is { } account)
                return JsonSerializer.Serialize(OfflineFinance.ProjectAccount(account, store), Json.Options);
        }

        // Accusés locaux : ne créent ni numéro de reçu officiel ni code QR valide.
        if (path.StartsWith("api/paiements/", StringComparison.Ordinal) &&
            Guid.TryParse(path["api/paiements/".Length..], out var receiptId) &&
            OfflineFinance.PendingReceipt(store, receiptId) is { } localReceipt)
            return JsonSerializer.Serialize(localReceipt, Json.Options);

        var pending = store.Outbox();
        if (pending.Count == 0) return snapshot;

        if (TryStudentId(path, out var studentId))
        {
            JsonObject? student = snapshot is null ? PendingRegistration(store, pending, studentId)
                : JsonNode.Parse(snapshot) as JsonObject;
            if (student is null) return snapshot;
            foreach (var operation in pending)
            {
                if (operation.Method != "PUT" || !string.Equals(operation.Path, path, StringComparison.Ordinal))
                    continue;
                if (JsonNode.Parse(operation.Body) is JsonObject fields)
                    CopyStudentFields(student, fields);
            }
            return student.ToJsonString(Json.Options);
        }

        // Les saisies de notes et de présences ont les mêmes clés métier que les
        // lignes de la copie reçue. Ne jamais synthétiser des dossiers absents.
        var isGradeSheet = path.StartsWith("api/scolarite/evaluations/", StringComparison.Ordinal)
            && path.EndsWith("/notes", StringComparison.Ordinal);
        var isAttendanceSheet = path.StartsWith("api/scolarite/presences?", StringComparison.Ordinal);
        if (snapshot is null || (!isGradeSheet && !isAttendanceSheet)) return snapshot;

        if (JsonNode.Parse(snapshot) is not JsonObject sheet || sheet["rows"] is not JsonArray rows)
            return snapshot;

        foreach (var operation in pending)
        {
            if (operation.Method != "PUT" || !string.Equals(operation.Path, path, StringComparison.Ordinal))
                continue;
            if (JsonNode.Parse(operation.Body) is not JsonArray changes) continue;
            foreach (var change in changes.OfType<JsonObject>())
            {
                if (change["enrollmentId"]?.ToString() is not { Length: > 0 } id) continue;
                var row = rows.OfType<JsonObject>().FirstOrDefault(r =>
                    string.Equals(r["enrollmentId"]?.ToString(), id, StringComparison.OrdinalIgnoreCase));
                if (row is null) continue;
                if (isGradeSheet)
                {
                    if (change.ContainsKey("score")) row["score"] = change["score"]?.DeepClone();
                    if (change.ContainsKey("comment")) row["comment"] = change["comment"]?.DeepClone();
                }
                else
                {
                    if (change.ContainsKey("status")) row["status"] = change["status"]?.DeepClone();
                    if (change.ContainsKey("note")) row["note"] = change["note"]?.DeepClone();
                }
            }
        }
        return sheet.ToJsonString(Json.Options);
    }

    /// <summary>
    /// Projection efficace des modifications nominatives dans le registre :
    /// tous les changements du journal sont déjà chargés en mémoire.
    /// </summary>
    public static StudentListItem ApplyStudentEdits(StudentListItem row, IReadOnlyList<OutboxItem> pending)
    {
        JsonObject? view = null;
        foreach (var item in pending)
        {
            if (item.Method != "PUT" || item.Path != $"api/apprenants/{row.Id}")
                continue;
            if (JsonNode.Parse(item.Body) is not JsonObject change) continue;
            view ??= JsonSerializer.SerializeToNode(row, Json.Options)!.AsObject();
            CopyStudentFields(view, change);
        }
        return view is null ? row : view.Deserialize<StudentListItem>(Json.Options) ?? row;
    }

    /// <summary>
    /// Inscriptions encore locales : matricule provisoire clairement distinct d'un
    /// matricule officiel, jamais de reçu ni d'inscription serveur fabriqués.
    /// </summary>
    public static IReadOnlyList<StudentListItem> PendingRegistrationRows(OfflineStore store)
    {
        var promotions = Promotions(store);
        var rows = new List<StudentListItem>();
        foreach (var item in store.Outbox())
        {
            if (item.Method != "POST" || item.Path != "api/apprenants" ||
                !Guid.TryParse(item.Id, out var localId)) continue;
            if (JsonNode.Parse(item.Body) is not JsonObject body ||
                body["identity"] is not JsonObject identity) continue;
            var promotionId = Guid.TryParse(body["promotionId"]?.ToString(), out var id) ? id : Guid.Empty;
            var promotionCode = promotions.FirstOrDefault(p => p.Id == promotionId)?.Code;
            rows.Add(new StudentListItem(localId, null, ProvisionalMatricule(item),
                Value(identity, "lastName"), Value(identity, "middleName"),
                Value(identity, "firstName"), Value(identity, "gender"), promotionCode,
                "En attente", Value(identity, "guardianPhone"), 0m, 0m));
        }
        return rows;
    }

    private static JsonObject? PendingRegistration(OfflineStore store, IReadOnlyList<OutboxItem> pending, Guid studentId)
    {
        var item = pending.FirstOrDefault(x => x.Method == "POST" &&
            x.Path == "api/apprenants" && Guid.TryParse(x.Id, out var id) && id == studentId);
        if (item is null || JsonNode.Parse(item.Body) is not JsonObject body ||
            body["identity"] is not JsonObject fields) return null;

        // Le matricule et l'identifiant d'inscription définitifs restent à attribuer.
        var student = new StudentDto(studentId, ProvisionalMatricule(item),
            Value(fields, "lastName"), Value(fields, "middleName"), Value(fields, "firstName"),
            Value(fields, "gender"), null, null, null, null, null, null,
            null, null, null, null,
            "Inscription locale en attente de confirmation par le serveur.", false,
            Array.Empty<EnrollmentDto>(), 0);
        var node = JsonSerializer.SerializeToNode(student, Json.Options)!.AsObject();
        CopyStudentFields(node, fields);
        return node;
    }

    private static string ProvisionalMatricule(OutboxItem item) => "LOCAL-" + item.Id[..Math.Min(8, item.Id.Length)].ToUpperInvariant();

    private static string Value(JsonObject obj, string key) => obj[key]?.ToString() ?? string.Empty;

    private static IReadOnlyList<PromotionDto> Promotions(OfflineStore store)
    {
        try
        {
            if (store.ReadPage("api/etablissement/promotions") is { } cached)
                return JsonSerializer.Deserialize<List<PromotionDto>>(cached.Json, Json.Options) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) { }
        return [];
    }

    private static bool TryStudentId(string path, out Guid studentId)
    {
        studentId = Guid.Empty;
        if (!path.StartsWith("api/apprenants/", StringComparison.Ordinal)) return false;
        var id = path["api/apprenants/".Length..];
        return Guid.TryParse(id, out studentId);
    }

    private static void CopyStudentFields(JsonObject target, JsonObject source)
    {
        foreach (var field in StudentFields)
            if (source.ContainsKey(field)) target[field] = source[field]?.DeepClone();
    }
}
