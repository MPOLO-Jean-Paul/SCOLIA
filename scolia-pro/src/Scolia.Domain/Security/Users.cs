using Scolia.Domain.Common;

namespace Scolia.Domain.Security;

/// <summary>Rôles persistés : ne jamais réutiliser ni renuméroter les valeurs historiques.</summary>
public enum Role { Administrateur = 0, Direction = 1, Secretariat = 2, Comptabilite = 3, Enseignant = 4, Promoteur = 5 }

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
    /// <summary>Compte enseignant : fiche du personnel dont il encode les cours attribués (délibération).</summary>
    public Guid? StaffMemberId { get; set; }
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
    /// <summary>Lecture de statistiques agrégées uniquement ; ne donne aucun accès aux reçus ou dossiers nominatifs.</summary>
    public const string PilotageRead = "pilotage.lire";
    public const string SettingsWrite = "parametres.ecrire";
    public const string UsersManage = "utilisateurs.gerer";
    public const string AuditRead = "audit.lire";
    public const string BackupManage = "sauvegardes.gerer";
    public const string AcademicsWrite = "scolarite.ecrire";
    public const string AcademicsRead = "scolarite.lire";
    public const string StaffManage = "personnel.gerer";
    public const string DisciplineCancel = "discipline.annuler";
    /// <summary>Jury : décisions motivées, clôture et réouverture des délibérations, codes anonymes.</summary>
    public const string DeliberationManage = "deliberation.gerer";
    /// <summary>Enseignant : encode et remet les cotes de ses seuls cours attribués, par code anonyme, sans voir les noms.</summary>
    public const string MarksEncode = "cotes.encoder";

    public static readonly IReadOnlyList<string> All =
    [
        StudentsRead, StudentsWrite, PaymentsRead, PaymentsCreate, PaymentsCancel, ExpensesWrite, CashClose,
        ReportsRead, PilotageRead, SettingsWrite, UsersManage, AuditRead, BackupManage, AcademicsWrite, AcademicsRead, StaffManage, DisciplineCancel, DeliberationManage,
        MarksEncode,
    ];

    /// <summary>Les seuls rôles qu'on attribue.</summary>
    public static readonly IReadOnlyList<Role> Assignable = [Role.Secretariat, Role.Comptabilite, Role.Promoteur, Role.Enseignant];

    /// <summary>Le secrétariat est l'administrateur : ses fonctions, le paramétrage, les utilisateurs, les sauvegardes et le journal.
    /// La comptabilité garde les finances.</summary>
    public static IReadOnlyCollection<string> For(Role role) => role switch
    {
        // Le secrétariat a tout, sauf l'espace enseignant (« mes cours ») qui n'a de sens que relié à une fiche du personnel.
        Role.Secretariat or Role.Administrateur or Role.Direction => All.Where(p => p != MarksEncode).ToList(),
        Role.Comptabilite => [StudentsRead, PaymentsRead, PaymentsCreate, PaymentsCancel, ExpensesWrite, CashClose, ReportsRead, AuditRead, BackupManage],
        // Seul le tableau de bord agrégé est autorisé côté API ; personnalisation d'affichage stockée sur l'appareil.
        Role.Promoteur => [PilotageRead],
        // Portail enseignant d'ITMCD : uniquement l'encodage de ses cours ; le service vérifie l'attribution à chaque appel.
        Role.Enseignant => [MarksEncode],
        _ => [],
    };

    public static string Label(Role role) => role switch
    {
        Role.Secretariat or Role.Administrateur or Role.Direction => "Secrétariat",
        Role.Comptabilite => "Comptabilité",
        Role.Promoteur => "Promoteur / Responsable",
        Role.Enseignant => "Enseignant",
        _ => role.ToString(),
    };
}

/// <summary>
/// Code d'accès choisi par la personne, comme sur Android : un code PIN (chiffres seulement, 6 à 16) ou un mot de passe
/// (8 caractères ou plus, lettres, chiffres ou symboles). Les codes trop faciles à deviner sont refusés ; après cinq
/// essais faux, le compte est de toute façon verrouillé quelques minutes.
/// </summary>
public static class PasswordPolicy
{
    public const int PinMinLength = 6;
    public const int PinMaxLength = 16;
    public const int PasswordMinLength = 8;
    public const int MaxLength = 128;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "motdepasse", "azertyuiop", "qwertyuiop", "azerty123", "password1", "motdepasse1",
        "scolia2026", "administrateur", "secretariat", "comptabilite", "abcdefgh", "abc12345", "00000000",
    };

    public static bool IsPin(string? code) => !string.IsNullOrEmpty(code) && code.All(char.IsAsciiDigit);

    public static void Validate(string? password)
    {
        var v = new ValidationBuilder();
        if (IsPin(password))
        {
            var pin = password!;
            v.Require(pin.Length >= PinMinLength, "motDePasse", $"Le code PIN doit compter au moins {PinMinLength} chiffres.");
            v.Require(pin.Length <= PinMaxLength, "motDePasse", $"Le code PIN compte au plus {PinMaxLength} chiffres.");
            v.Require(!IsTrivialPin(pin), "motDePasse", "Ce code est trop facile à deviner (chiffres identiques ou qui se suivent).");
        }
        else
        {
            v.Require(!string.IsNullOrEmpty(password) && password.Length >= PasswordMinLength, "motDePasse",
                $"Le mot de passe doit contenir au moins {PasswordMinLength} caractères.");
            if (!string.IsNullOrEmpty(password))
            {
                v.Require(password.Length <= MaxLength, "motDePasse", "Le mot de passe est trop long.");
                v.Require(!string.IsNullOrWhiteSpace(password), "motDePasse", "Le mot de passe ne peut pas être fait que d'espaces.");
                v.Require(!Common.Contains(password), "motDePasse", "Ce mot de passe est trop courant : choisissez-en un autre.");
            }
        }
        v.ThrowIfInvalid("Code d'accès trop faible.");
    }

    /// <summary>111111, 123456, 987654… : refusés, comme sur un téléphone.</summary>
    public static bool IsTrivialPin(string pin)
    {
        if (pin.Length < 2) return true;
        if (pin.All(c => c == pin[0])) return true;
        bool Step(int delta)
        {
            for (var i = 1; i < pin.Length; i++)
                if ((pin[i] - pin[i - 1] + 10) % 10 != (delta + 10) % 10) return false;
            return true;
        }
        return Step(1) || Step(-1);
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
