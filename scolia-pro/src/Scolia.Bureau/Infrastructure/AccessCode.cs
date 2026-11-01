namespace Scolia.Bureau.Infrastructure;

/// <summary>Une règle du code d'accès, cochée en direct pendant la saisie.</summary>
public sealed record AccessCodeRule(string Text, bool Met);

/// <summary>
/// Code d'accès au choix, comme sur Android : code PIN (chiffres seulement) ou mot de passe. Mêmes règles que le
/// serveur, qui reste seul juge : le client les montre pendant la saisie pour éviter un refus après coup.
/// </summary>
public static class AccessCode
{
    public const int PinMinLength = 6;
    public const int PinMaxLength = 16;
    public const int PasswordMinLength = 8;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "motdepasse", "azertyuiop", "qwertyuiop", "azerty123", "password1", "motdepasse1",
        "scolia2026", "administrateur", "secretariat", "comptabilite", "abcdefgh", "abc12345", "00000000",
    };

    public static bool IsPin(string code) => code.Length > 0 && code.All(char.IsAsciiDigit);

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

    /// <summary>Les règles de la méthode choisie, cochées selon la saisie.</summary>
    public static IReadOnlyList<AccessCodeRule> Rules(string code, bool pin) => pin
        ?
        [
            new($"{PinMinLength} à {PinMaxLength} chiffres", code.Length is >= PinMinLength and <= PinMaxLength && IsPin(code)),
            new("Chiffres seulement", code.Length > 0 && IsPin(code)),
            new("Ni chiffres identiques ni suite (111111, 123456)", IsPin(code) && code.Length >= PinMinLength && !IsTrivialPin(code)),
        ]
        :
        [
            new($"Au moins {PasswordMinLength} caractères", code.Length >= PasswordMinLength && code.Length <= 128),
            new("Pas seulement des chiffres (sinon, choisissez « Code PIN »)", code.Length > 0 && !IsPin(code)),
            new("Pas un mot de passe courant", code.Length > 0 && !Common.Contains(code) && !string.IsNullOrWhiteSpace(code)),
        ];

    /// <summary>Premier problème du code choisi, ou rien s'il convient.</summary>
    public static string? Problem(string code)
    {
        if (IsPin(code))
        {
            if (code.Length < PinMinLength) return $"Le code PIN doit compter au moins {PinMinLength} chiffres.";
            if (code.Length > PinMaxLength) return $"Le code PIN compte au plus {PinMaxLength} chiffres.";
            if (IsTrivialPin(code)) return "Ce code est trop facile à deviner (chiffres identiques ou qui se suivent).";
            return null;
        }
        if (code.Length < PasswordMinLength) return $"Le mot de passe doit contenir au moins {PasswordMinLength} caractères.";
        if (string.IsNullOrWhiteSpace(code)) return "Le mot de passe ne peut pas être fait que d'espaces.";
        if (Common.Contains(code)) return "Ce mot de passe est trop courant : choisissez-en un autre.";
        return null;
    }

    /// <summary>Phrase d'aide commune aux écrans qui créent un compte.</summary>
    public const string Hint = "Code PIN de 6 chiffres ou plus, ou mot de passe de 8 caractères ou plus.";
}
