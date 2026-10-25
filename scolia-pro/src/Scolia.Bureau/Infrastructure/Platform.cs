namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Services du système qui diffèrent entre l'ordinateur et le téléphone. L'application Android les fournit au
/// démarrage ; sur l'ordinateur, ils restent vides et SCOLIA utilise Windows directement.
/// </summary>
public static class Platform
{
    /// <summary>Vrai dans l'application du téléphone : pas de serveur sur l'appareil, il se connecte à celui de l'établissement.</summary>
    public static bool IsMobile { get; set; }

    /// <summary>Ouvre un document enregistré, un PDF ou un classeur, avec l'application du téléphone.</summary>
    public static Action<string>? OpenFile { get; set; }

    /// <summary>Ouvre une adresse : WhatsApp, SMS, appel, e-mail ou page web.</summary>
    public static Action<string>? OpenUri { get; set; }

    /// <summary>Dossier où les documents sont enregistrés sur le téléphone.</summary>
    public static string? DocumentsFolder { get; set; }

    /// <summary>Joue un son au format WAV, 16 bits mono : les notes de SCOLIA sur le haut-parleur du téléphone.</summary>
    public static Action<byte[]>? PlayWave { get; set; }

    /// <summary>Nom de l'appareil, affiché au serveur lors de la demande d'accès.</summary>
    public static string? DeviceName { get; set; }
}
