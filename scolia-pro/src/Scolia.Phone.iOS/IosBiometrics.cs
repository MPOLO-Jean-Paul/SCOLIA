using Foundation;
using LocalAuthentication;
using Scolia.Bureau.Infrastructure;
using Security;

namespace Scolia.Phone.iOS;

/// <summary>
/// Face ID et Touch ID : à l'activation, SCOLIA dépose l'accès au compte (vérifié par le serveur) dans le trousseau,
/// lié aux visages ou empreintes enregistrés à cet instant. Seul un déverrouillage biométrique réussi le rend
/// lisible ; un nouveau visage ou une nouvelle empreinte ajoutés ensuite l'effacent, comme sur Android.
/// </summary>
public static class IosBiometrics
{
    private const string Service = "com.jpservices.scolia.ios.biometric";
    // v2 : l'accès scellé remplace l'ancienne simple preuve, qui dépendait d'une session effacée à la déconnexion.
    private const string Account = "scolia-biometric-access-v2";
    private const string OldAccount = "scolia-biometric-proof-v1";
    private const string FlagKey = "scolia.biometric.access.v2";

    /// <summary>« Face ID », « Touch ID » ou un libellé neutre si l'appareil n'en a pas.</summary>
    public static string DisplayName()
    {
        using var context = new LAContext();
        context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out _);
        return context.BiometryType switch
        {
            LABiometryType.FaceId => "Face ID",
            LABiometryType.TouchId => "Touch ID",
            _ => "Face ID ou Touch ID",
        };
    }

    public static bool IsAvailable()
    {
        using var context = new LAContext();
        return context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out _);
    }

    public static bool HasProof() => NSUserDefaults.StandardUserDefaults.BoolForKey(FlagKey);

    /// <summary>Activation : l'utilisateur confirme d'abord avec Face ID ou Touch ID, puis l'accès est déposé.</summary>
    public static async Task<bool> EnrollAsync(string sealedAccess)
    {
        if (!await AskAsync("Activer le déverrouillage de SCOLIA Phone").ConfigureAwait(false)) return false;
        return await Task.Run(() =>
        {
            Remove();
            using var access = new SecAccessControl(SecAccessible.WhenPasscodeSetThisDeviceOnly, SecAccessControlCreateFlags.BiometryCurrentSet);
            var record = new SecRecord(SecKind.GenericPassword)
            {
                Service = Service,
                Account = Account,
                ValueData = NSData.FromArray(System.Text.Encoding.UTF8.GetBytes(sealedAccess)),
                AccessControl = access,
            };
            if (SecKeyChain.Add(record) != SecStatusCode.Success) return false;
            NSUserDefaults.StandardUserDefaults.SetBool(true, FlagKey);
            return true;
        }).ConfigureAwait(false);
    }

    /// <summary>Déverrouillage : lire l'accès fait apparaître Face ID ou Touch ID ; un échec ne révèle rien.</summary>
    public static Task<string?> UnlockAsync()
    {
        if (!HasProof()) return Task.FromResult<string?>(null);
        // La lecture d'un élément protégé attend l'utilisateur : jamais sur le fil de l'interface.
        return Task.Run<string?>(() =>
        {
            var query = new SecRecord(SecKind.GenericPassword)
            {
                Service = Service,
                Account = Account,
                UseOperationPrompt = "Confirmer l'accès à votre session SCOLIA Phone",
            };
            var data = SecKeyChain.QueryAsData(query, false, out var status);
            if (status == SecStatusCode.ItemNotFound)
            {
                // Visage ou empreinte modifiés : l'accès n'existe plus, le code redevient nécessaire.
                Disable();
                return null;
            }
            return status == SecStatusCode.Success && data is { Length: > 0 }
                ? System.Text.Encoding.UTF8.GetString(data.ToArray()) : null;
        });
    }

    public static void Disable()
    {
        Remove();
        NSUserDefaults.StandardUserDefaults.RemoveObject(FlagKey);
        NSUserDefaults.StandardUserDefaults.RemoveObject("scolia.biometric.enabled");
        BiometricAccess.Disable();
    }

    private static void Remove()
    {
        SecKeyChain.Remove(new SecRecord(SecKind.GenericPassword) { Service = Service, Account = Account });
        SecKeyChain.Remove(new SecRecord(SecKind.GenericPassword) { Service = Service, Account = OldAccount });
    }

    private static async Task<bool> AskAsync(string reason)
    {
        using var context = new LAContext();
        if (!context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out _)) return false;
        try
        {
            var (accepted, error) = await context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, reason).ConfigureAwait(false);
            return accepted && error is null;
        }
        catch (NSErrorException)
        {
            return false;
        }
    }
}
