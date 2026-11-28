using System.Security.Cryptography;
using System.Text;

namespace Scolia.Domain.Common;

/// <summary>Contrat partagé entre le client et le serveur : empreinte des lignes et mensualités affichées.</summary>
public static class MonthlyExportSelection
{
    public static string Fingerprint(IEnumerable<(Guid StudentId, Guid? FeeItemId)> rows)
    {
        var ordered = rows.Select(r => $"{r.StudentId:D}/{r.FeeItemId:D}").Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ordered))));
    }
}
