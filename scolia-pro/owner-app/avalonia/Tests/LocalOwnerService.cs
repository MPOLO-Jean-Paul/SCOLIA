using System.Diagnostics;

namespace JPServices.Owner.Tests;

/// <summary>Vrai Worker et migrations SQLite, exposés seulement sur une adresse locale de test.</summary>
internal sealed class LocalOwnerService : IDisposable
{
    private readonly Process process;
    internal Uri Address { get; }
    internal static OwnerCredentials Credentials =>
        new("abcd1234.access", "service-secret-fictif-2026", "owner-console-integration-fictif-2026");

    internal LocalOwnerService()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Scolia.sln"))) directory = directory.Parent;
        var script = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Sources SCOLIA introuvables."),
            "deploy", "owner-console", "tests", "fixtures", "native-service.mjs");
        var start = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(script);
        process = Process.Start(start) ?? throw new InvalidOperationException("Serveur de démonstration non démarré.");
        var address = process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        if (address is null || !Uri.TryCreate(address, UriKind.Absolute, out var parsed))
            throw new InvalidOperationException("Serveur de démonstration indisponible : " + process.StandardError.ReadToEnd());
        Address = parsed;
    }

    public void Dispose()
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        process.WaitForExit(5000);
        process.Dispose();
    }
}
