using System.Security.Principal;

namespace Moyai.Tray;

internal static class TrayInstance
{
    internal static string Name
    {
        get
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("Current Windows SID is unavailable.");
            return @"Local\Akatsukisoft.Moyai.Tray." + sid;
        }
    }

    internal static bool TryRun(Action run, string? instanceName = null)
    {
        using var mutex = new Mutex(false, instanceName ?? Name);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        // 前のトレイが異常終了した場合も、取得済みの所有権を使って再起動できます。
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return false;
        try
        {
            run();
            return true;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
