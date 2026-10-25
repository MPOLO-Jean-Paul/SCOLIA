using Scolia.Domain.Common;

namespace Scolia.Domain.Security;

/// <summary>Deux comptes, comme dans la V79 : le secrétariat administre SCOLIA, la comptabilité tient la caisse.
/// Administrateur, Direction et Enseignant ne servent plus qu'à relire une ancienne base ; ils sont convertis au démarrage.</summary>
public enum Role { Administrateur = 0, Direction = 1, Secretariat = 2, Comptabilite = 3, Enseignant = 4 }

public sealed class AppUser : Entity
{
    public string UserName { get; set; } = string.Empty;
    public string NormalizedUserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Role Role { get; set; }
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    /// <summary>Change à chaque modification de sécurité : invalide les sessions ouvertes.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
}

/// <summary>Permissions fines. Les rôles sont des ensembles de permissions.</summary>
public static class Permissions
{
    public const string StudentsRead = "apprenants.lire";
    public const string StudentsWrite = "apprenants.ecrire";
    public const string PaymentsRead = "paiements.lire";
    public const string PaymentsCreate = "paiements.encaisser";
    public const string PaymentsCancel = "paiements.annuler";
    public const string ExpensesWrite = "depenses.ecrire";
    public const string CashClose = "caisse.arreter";
    public const string ReportsRead = "rapports.lire";
    public const string SettingsWrite = "parametres.ecrire";
    public const string UsersManage = "utilisateurs.gerer";
    public const string AuditRead = "audit.lire";
    public const string BackupManage = "sauvegardes.gerer";
    public const string AcademicsWrite = "scolarite.ecrire";
    public const string AcademicsRead = "scolarite.lire";
    public const string StaffManage = "personnel.gerer";
    public const string DisciplineCancel = "discipline.annuler";

    public static readonly IReadOnlyList<string> All =
    [
        StudentsRead, StudentsWrite, PaymentsRead, PaymentsCreate, PaymentsCancel, ExpensesWrite, CashClose,
        ReportsRead, SettingsWrite, UsersManage, AuditRead, BackupManage, AcademicsWrite, AcademicsRead, StaffManage, DisciplineCancel,
    ];

    /// <summary>Les seuls rôles qu'on attribue.</summary>
    public static readonly IReadOnlyList<Role> Assignable = [Role.Secretariat, Role.Comptabilite];

    /// <summary>Le secrétariat est l'administrateur : ses fonctions, le paramétrage, les utilisateurs, les sauvegardes et le journal.
    /// La comptabilité garde les finances.</summary>
    public static IReadOnlyCollection<string> For(Role role) => role switch
    {
        Role.Secretariat or Role.Administrateur or Role.Direction => All,
        Role.Comptabilite => [StudentsRead, PaymentsRead, PaymentsCreate, PaymentsCancel, ExpensesWrite, CashClose, ReportsRead, AuditRead, BackupManage],
        _ => [],
    };

    public static string Label(Role role) => role switch
    {
        Role.Secretariat or Role.Administrateur or Role.Direction => "Secrétariat",
        Role.Comptabilite => "Comptabilité",
        Role.Enseignant => "Enseignant (ancien rôle)",
        _ => role.ToString(),
    };
}

public static class PasswordPolicy
{
    public static void Validate(string? password)
    {
        var v = new ValidationBuilder();
        v.Require(!string.IsNullOrEmpty(password) && password.Length >= 8, "motDePasse", "Le mot de passe doit contenir au moins 8 caractères.");
        if (!string.IsNullOrEmpty(password))
        {
            v.Require(password.Any(char.IsLetter) && password.Any(char.IsDigit), "motDePasse", "Le mot de passe doit contenir des lettres et des chiffres.");
            v.Require(password.Length <= 128, "motDePasse", "Le mot de passe est trop long.");
        }
        v.ThrowIfInvalid("Mot de passe trop faible.");
    }
}

/// <summary>Journal d'audit chaîné : chaque entrée contient l'empreinte de la précédente.
/// Toute modification ou suppression ultérieure d'une entrée est détectable.</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Data { get; set; }
    public string? IpAddress { get; set; }
    public string PreviousHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;

    public string ComputeHash()
    {
        var payload = string.Join('\u001F', Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTime.SpecifyKind(AtUtc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture), UserName, Action, EntityType, EntityId ?? string.Empty,
            Summary, Data ?? string.Empty, IpAddress ?? string.Empty, PreviousHash);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)));
    }
}
