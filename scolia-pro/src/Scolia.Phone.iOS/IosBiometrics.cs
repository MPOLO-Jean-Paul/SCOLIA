using System.Security.Cryptography;
using Foundation;
using LocalAuthentication;
using Scolia.Bureau.Infrastructure;
using Security;

namespace Scolia.Phone.iOS;

/// <summary>
/// Face ID et Touch ID : à l'activation, SCOLIA dépose une preuve dans le trousseau, liée aux visages ou empreintes
/// enregistrés à cet instant. Seul un déverrouillage biométrique réussi la rend lisible ; un nouveau visage ou une
/// nouvelle empreinte ajoutés ensuite l'effacent, comme sur Android.
/// </summary>
public static class IosBiometrics
{
    private const string Service = "com.jpservices.scolia.ios.biometric";
    private const string Account = "scolia-biometric-proof-v1";
    private const string FlagKey = "scolia.biometric.enabled";

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

    /// <summary>Activation : l'utilisateur confirme d'abord avec Face ID ou Touch ID, puis la preuve est déposée.</summary>
    public static async Task<bool> EnrollAsync()
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
                ValueData = NSData.FromArray(RandomNumberGenerator.GetBytes(32)),
                AccessControl = access,
            };
            if (SecKeyChain.Add(record) != SecStatusCode.Success) return false;
            NSUserDefaults.StandardUserDefaults.SetBool(true, FlagKey);
            return true;
        }).ConfigureAwait(false);
    }

    /// <summary>Déverrouillage : lire la preuve fait apparaître Face ID ou Touch ID ; un échec ne révèle rien.</summary>
    public static Task<bool> VerifyAsync()
    {
        if (!HasProof()) return Task.FromResult(false);
        // La lecture d'un élément protégé attend l'utilisateur : jamais sur le fil de l'interface.
        return Task.Run(() =>
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
                // Visage ou empreinte modifiés : la preuve n'existe plus, le mot de passe redevient nécessaire.
                Disable();
                return false;
            }
            return status == SecStatusCode.Success && data is not null && data.Length > 0;
        });
    }

    public static void Disable()
    {
        Remove();
        NSUserDefaults.StandardUserDefaults.RemoveObject(FlagKey);
        BiometricAccess.Disable();
    }

    private static void Remove() =>
        SecKeyChain.Remove(new SecRecord(SecKind.GenericPassword) { Service = Service, Account = Account });

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
