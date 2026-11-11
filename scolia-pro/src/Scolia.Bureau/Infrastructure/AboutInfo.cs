namespace Scolia.Bureau.Infrastructure;

/// <summary>Éditeur et développeur de SCOLIA, repris de la page « À propos » de la version 79.</summary>
public static class AboutInfo
{
    public const string Publisher = "Jp-Services";
    public const string Developer = "Jean Paul MPOLO LONO";
    public const string Role = "Programmeur et concepteur de SCOLIA";
    public const string Phone = "+243 977 152 825";
    public const string Email = "jeanpaulmpolo242@gmail.com";
    public const string Tagline = "Gestion scolaire et comptable";

    public const string Description =
        "Gestion des apprenants, des promotions, des mensualités et des prises en charge, cartes d'accès, cotes et délibération. " +
        "Espace de caisse avec recettes, dépenses, journal et reçus numérotés vérifiables par QR. " +
        "Les postes de l'établissement partagent les mêmes données sur le réseau local ; rien n'est envoyé à un tiers.";

    /// <summary>Les grands domaines du logiciel, affichés en pastilles.</summary>
    public static readonly IReadOnlyList<string> Modules =
    [
        "Apprenants et inscriptions", "Encaissements et reçus", "Suivi mensuel et recouvrement", "Caisse et dépenses",
        "Cours, notes et résultats", "Présences et discipline", "Personnel et paie", "Cartes d'accès", "Sauvegardes",
    ];

    public static string Version => typeof(AboutInfo).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";
    public static string Copyright => $"© {DateTime.Today.Year} {Publisher}. Tous droits réservés.";

    /// <summary>Numéro sans espaces ni « + », pour les liens WhatsApp et téléphone.</summary>
    public static string PhoneDigits => new(Phone.Where(char.IsDigit).ToArray());
}
