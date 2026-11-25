#if SCOLIA_FLUTTER_MIGRATION_TEST
using Android.App;
using Scolia.Bureau.Api;
using Scolia.Bureau.Infrastructure;

namespace Scolia.Mobile;

// Exclusively compiled into the x86_64 emulator validation APK. Never operates
// on a production installation or a real school. Uses the real Android Keystore
// and the same default SQLite storage as the installed SCOLIA client.
internal static class FlutterMigrationProbe
{
    private const string Account = "validation-migration";
    private const string Code = "SyntheticOnly123!";
    private const string Page = "{\"test\":\"migration-1.2.99\"}";
    private const string Operation = "migration-test-payment";
    private const string Checkpoint = "migration-test-revision";
    private const string Path = "api/apprenants";
    private static readonly Guid Identity = Guid.Parse("f4ca74a5-8a03-43f5-9a41-780d01029300");

    internal static void Check(Activity activity)
    {
        if (activity.PackageName != "com.jpservices.scolia.fluttervalidation")
            throw new InvalidOperationException("Migration test is not isolated.");
        var store = new OfflineStore(new Uri("http://scolia-migration.invalid:5080/")) { User = Account };
#if SCOLIA_FLUTTER_MIGRATION_SEED
        if (store.Outbox().Count != 0) throw new InvalidOperationException("Seed requires a clean test installation.");
        store.SavePage(Path, Page);
        store.SaveCredential(Account, Code, new SessionDto(Identity, Account, "Validation", "Secretariat",
            "Validation", [], false, "Etablissement fictif", null, null, "FC", "243"));
        store.RememberAccount(Account);
        store.Enqueue(new OutboxItem { Id = Operation, Key = Operation, Path = "api/paiements", Body = "{\"test\":true}" });
        store.SaveSyncCheckpointFor(Account, Checkpoint);
        Android.Util.Log.Info("SCOLIA-Migration", "validation:legacy-seeded");
#else
        // No write/seed before the assertions: the old installation must provide every value.
        if (store.ReadPage(Path)?.Json != Page || store.LastAccount != Account ||
            store.OfflineLogin(Account, Code) is null || store.OfflineLogin(Account, "WrongCode") is not null ||
            store.ReadSyncCheckpointFor(Account) != Checkpoint ||
            store.Outbox().Count != 1 || store.Outbox()[0].Key != Operation ||
            store.Outbox()[0].Body != "{\"test\":true}")
            throw new InvalidOperationException("Encrypted SQLite, account or pending operation lost during update.");
        store.User = "other-validation-account";
        if (store.ReadPage(Path) is not null || store.Outbox().Count != 0)
            throw new InvalidOperationException("Account isolation lost after update.");
        Android.Util.Log.Info("SCOLIA-Migration", "validation:encrypted-data-preserved");
#endif
    }
}
#endif
