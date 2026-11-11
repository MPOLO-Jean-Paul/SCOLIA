using System.Runtime.InteropServices;

namespace Scolia.Api;

/// <summary>
/// Ordinateur principal sous Windows : tant que le serveur partage les données avec les autres postes,
/// Windows ne met pas l'ordinateur en veille. L'écran, lui, peut s'éteindre normalement.
/// La demande vit sur un fil dédié : Windows l'attache au fil qui l'a faite et la retire à sa fin.
/// </summary>
internal static class KeepAwake
{
    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000,
    }

    [DllImport("kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState state);

    /// <summary>Garde l'ordinateur éveillé jusqu'à l'arrêt du serveur. Sans effet hors de Windows.</summary>
    public static void Hold(CancellationToken stopping)
    {
        if (!OperatingSystem.IsWindows()) return;
        var thread = new Thread(() =>
        {
            try
            {
                SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);
                stopping.WaitHandle.WaitOne();
            }
            finally
            {
                // Rendre la main à Windows : la veille habituelle reprend.
                SetThreadExecutionState(ExecutionState.Continuous);
            }
        })
        {
            IsBackground = true,
            Name = "SCOLIA · ordinateur éveillé",
        };
        thread.Start();
    }
}
