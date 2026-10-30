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
/// Preuve biométrique par opération Android Keystore. Le téléphone conserve
/// uniquement une preuve chiffrée. Aucun gabarit d'empreinte ou mot de passe
/// n'entre dans SCOLIA ; l'authentification reste gérée par Android.
/// </summary>
public static class AndroidBiometrics
{
    private const string Alias = "scolia-biometric-proof-v2";
    private const string PreferencesName = "scolia-biometric-proof-v2";
    private const string ProofKey = "sealed-proof";
    private const string ProofText = "SCOLIA-PHONE-BIOMETRIC-v2";
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

    /// <summary>À appeler exclusivement après une connexion SCOLIA par mot de passe réussie et consentie.</summary>
    public static async Task<bool> EnrollAsync()
    {
        if (!IsAvailable() || Prefs is not { } prefs) return false;
        try
        {
            using var key = ReadKey(true);
            if (key is null) return false;
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, key);
            var authorized = await PromptAsync(cipher, "Activer l'empreinte pour ce compte");
            if (authorized is null) return false;
            var iv = authorized.GetIV();
            var bytes = authorized.DoFinal(Encoding.UTF8.GetBytes(ProofText));
            if (iv is not { Length: 12 } || bytes is null) return false;
            byte[] sealedProof = [.. iv, .. bytes];
            return prefs.Edit()!.PutString(ProofKey, Convert.ToBase64String(sealedProof))!.Commit();
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or GeneralSecurityException
                                   or InvalidOperationException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Déchiffrement uniquement si Android a autorisé l'opération biométrique forte.</summary>
    public static async Task<bool> VerifyAsync()
    {
        if (!IsAvailable() || Prefs is not { } prefs) return false;
        var saved = prefs.GetString(ProofKey, null);
        if (string.IsNullOrEmpty(saved)) return false; // jamais de création d'une clé au déverrouillage
        try
        {
            byte[] proof = Convert.FromBase64String(saved);
            if (proof.Length < 28) return false;
            using var key = ReadKey(false);
            if (key is null) return false;
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            using var parameters = new GCMParameterSpec(128, proof[..12]);
            cipher.Init(CipherMode.DecryptMode, key, parameters);
            var authorized = await PromptAsync(cipher, "Ouvrir mon espace SCOLIA");
            if (authorized is null) return false;
            var clear = authorized.DoFinal(proof[12..]);
            var expected = Encoding.UTF8.GetBytes(ProofText);
            return clear is not null && CryptographicOperations.FixedTimeEquals(clear, expected);
        }
        catch (KeyPermanentlyInvalidatedException)
        {
            // Toute nouvelle empreinte ou suppression du verrou Android exige le mot de passe SCOLIA.
            Disable();
            return false;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or GeneralSecurityException
                                   or FormatException or InvalidOperationException or CryptographicException)
        {
            return false;
        }
    }

    public static void Disable()
    {
        Prefs?.Edit()?.Remove(ProofKey)?.Commit();
        try
        {
            using var store = KeyStore.GetInstance("AndroidKeyStore")!;
            store.Load(null);
            if (store.ContainsAlias(Alias)) store.DeleteEntry(Alias);
        }
        catch (Java.Lang.Exception) { /* Le lien SCOLIA reste désactivé. */ }
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
