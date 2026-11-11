using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using System.Security.Cryptography;
using System.Text;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile.Security;

/// <summary>
/// Accès par empreinte avec Android Keystore : à l'activation, l'accès SCOLIA (compte et code vérifiés par le serveur)
/// est chiffré par une clé de la puce de sécurité, utilisable seulement après une empreinte forte. Une nouvelle
/// empreinte enregistrée dans le téléphone détruit la clé : le code redevient nécessaire. SCOLIA ne voit jamais
/// l'empreinte elle-même.
/// </summary>
public static class AndroidBiometrics
{
    // v3 : l'accès scellé remplace l'ancienne simple preuve, qui dépendait d'une session effacée à la déconnexion.
    private const string Alias = "scolia-biometric-access-v3";
    private const string PreferencesName = "scolia-biometric-access-v3";
    private const string ProofKey = "sealed-access";
    private const string OldAlias = "scolia-biometric-proof-v2";
    private const string OldPreferences = "scolia-biometric-proof-v2";
    private static WeakReference<Activity>? current;
    private static int inProgress;

    public static void Attach(Activity activity) => current = new WeakReference<Activity>(activity);

    public static void Detach(Activity activity)
    {
        if (current?.TryGetTarget(out var active) == true && ReferenceEquals(active, activity))
            current = null;
    }

    private static Activity? Active => current?.TryGetTarget(out var a) == true
        && !a.IsFinishing && !a.IsDestroyed ? a : null;

    private static ISharedPreferences? Prefs =>
        Active?.GetSharedPreferences(PreferencesName, FileCreationMode.Private);

    public static bool HasProof() => Prefs?.GetString(ProofKey, null) is { Length: > 0 };

    public static bool IsAvailable()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Q || Active is not { } activity)
            return false;
        try
        {
            if (activity.GetSystemService(Context.KeyguardService) is not KeyguardManager guard
                || !guard.IsDeviceSecure) return false;
            if (activity.GetSystemService(Context.BiometricService) is not BiometricManager manager)
                return false;
            // CryptoObject n'accepte que BIOMETRIC_STRONG (classe 3).
            return (int)(Build.VERSION.SdkInt >= BuildVersionCodes.R
                ? manager.CanAuthenticate((int)BiometricManagerAuthenticators.BiometricStrong)
                : manager.CanAuthenticate()) == 0;
        }
        catch (Java.Lang.Exception) { return false; }
    }

    private static IKey? ReadKey(bool create)
    {
        using var store = KeyStore.GetInstance("AndroidKeyStore")!;
        store.Load(null);
        if (!store.ContainsAlias(Alias))
        {
            if (!create) return null;
            using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
            var builder = new KeyGenParameterSpec.Builder(
                Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)!
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)!
                .SetKeySize(256)!
                .SetUserAuthenticationRequired(true)!
                .SetInvalidatedByBiometricEnrollment(true)!
                .SetRandomizedEncryptionRequired(true)!;
            // -1: chaque opération cryptographique exige une authentification.
            builder.SetUserAuthenticationValidityDurationSeconds(-1);
            generator.Init(builder.Build());
            generator.GenerateKey();
        }
        return store.GetKey(Alias, null);
    }

    private static async Task<Cipher?> PromptAsync(Cipher cipher, string message)
    {
        if (Active is not { } activity || Interlocked.Exchange(ref inProgress, 1) != 0)
            return null;
        using var cancel = new CancellationSignal();
        try
        {
            var done = new TaskCompletionSource<Cipher?>(TaskCreationOptions.RunContinuationsAsynchronously);
            activity.RunOnUiThread(() =>
            {
                try
                {
                    var builder = new BiometricPrompt.Builder(activity)
                        .SetTitle("SCOLIA Pro · Accès protégé")
                        .SetSubtitle(message)
                        .SetNegativeButton("Utiliser le mot de passe", activity.MainExecutor,
                            new CancelListener(done));
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                        builder.SetAllowedAuthenticators((int)BiometricManagerAuthenticators.BiometricStrong);
                    var prompt = builder.Build();
                    prompt.Authenticate(new BiometricPrompt.CryptoObject(cipher), cancel,
                        activity.MainExecutor, new AuthCallback(done));
                }
                catch (Exception ex) when (ex is Java.Lang.Exception or InvalidOperationException)
                {
                    done.TrySetResult(null);
                }
            });
            try { return await done.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false); }
            catch (TimeoutException) { cancel.Cancel(); return null; }
        }
        finally { Interlocked.Exchange(ref inProgress, 0); }
    }

    /// <summary>À appeler seulement avec un accès que le serveur vient de valider, et avec l'accord de l'utilisateur.</summary>
    public static async Task<bool> EnrollAsync(string access)
    {
        if (!IsAvailable() || Prefs is not { } prefs) return false;
        try
        {
            RemoveKey(OldAlias);
            Active?.GetSharedPreferences(OldPreferences, FileCreationMode.Private)?.Edit()?.Clear()?.Commit();
            // Nouvelle clé à chaque activation : un ancien accès scellé ne peut plus jamais être relu.
            RemoveKey(Alias);
            using var key = ReadKey(true);
            if (key is null) return false;
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
            var authorized = await PromptAsync(cipher, "Confirmez votre empreinte pour l'associer à votre compte");
            if (authorized is null) return false;
            var iv = authorized.GetIV();
            var bytes = authorized.DoFinal(Encoding.UTF8.GetBytes(access));
            if (iv is not { Length: 12 } || bytes is null) return false;
            byte[] sealedAccess = [.. iv, .. bytes];
            return prefs.Edit()!.PutString(ProofKey, Convert.ToBase64String(sealedAccess))!.Commit();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or GeneralSecurityException
                                   or InvalidOperationException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Rend l'accès scellé, seulement si Android a validé une empreinte forte ; sinon rien.</summary>
    public static async Task<string?> UnlockAsync()
    {
        if (!IsAvailable() || Prefs is not { } prefs) return null;
        var saved = prefs.GetString(ProofKey, null);
        if (string.IsNullOrEmpty(saved)) return null; // jamais de création d'une clé au déverrouillage
        try
        {
            byte[] sealedAccess = Convert.FromBase64String(saved);
            if (sealedAccess.Length < 28) return null;
            using var key = ReadKey(false);
            if (key is null) return null;
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            using var parameters = new GCMParameterSpec(128, sealedAccess[..12]);
            cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key, parameters);
            var authorized = await PromptAsync(cipher, "Ouvrir mon espace SCOLIA");
            if (authorized is null) return null;
            var clear = authorized.DoFinal(sealedAccess[12..]);
            return clear is { Length: > 0 } ? Encoding.UTF8.GetString(clear) : null;
        }
        catch (KeyPermanentlyInvalidatedException)
        {
            // Toute nouvelle empreinte ou suppression du verrou Android exige à nouveau le code SCOLIA.
            Disable();
            return null;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or GeneralSecurityException
                                   or FormatException or InvalidOperationException or CryptographicException)
        {
            return null;
        }
    }

    private static void RemoveKey(string alias)
    {
        try
        {
            using var store = KeyStore.GetInstance("AndroidKeyStore")!;
            store.Load(null);
            if (store.ContainsAlias(alias)) store.DeleteEntry(alias);
        }
        catch (Java.Lang.Exception) { /* Rien à retirer. */ }
    }

    public static void Disable()
    {
        Prefs?.Edit()?.Remove(ProofKey)?.Commit();
        RemoveKey(Alias);
        RemoveKey(OldAlias);
        BiometricAccess.Disable();
    }

    private sealed class CancelListener(TaskCompletionSource<Cipher?> done)
        : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which) => done.TrySetResult(null);
    }

    private sealed class AuthCallback(TaskCompletionSource<Cipher?> done)
        : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result)
            => done.TrySetResult(result?.CryptoObject?.Cipher);

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? text)
            => done.TrySetResult(null);
    }
}
