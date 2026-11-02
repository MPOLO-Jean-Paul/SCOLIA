namespace Scolia.Bureau.Infrastructure;

/// <summary>
/// Serveur SCOLIA de ce poste, quand il porte la base de l'établissement : le programme SCOLIA-Serveur sur l'ordinateur,
/// le serveur démarré dans l'application sur un téléphone. Le reste de l'application ne fait pas la différence.
/// </summary>
public interface ILocalServer
{
    /// <summary>Démarre le serveur s'il ne tourne pas ; SCOLIA attend ensuite qu'il réponde.</summary>
    void Start();

    /// <summary>Relance le serveur pour qu'il prenne un nouveau réglage.</summary>
    void Restart();

    /// <summary>Arrête le serveur lancé par cette application.</summary>
    void Stop();
}
