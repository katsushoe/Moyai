using System.Security.Principal;
using Moyai.Tray;

namespace Moyai.Mcp.Tests;

public sealed class TrayInstanceTests
{
    [Fact]
    public void NameUsesCurrentUserAndWindowsSessionNamespace()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        Assert.Equal(@"Local\Akatsukisoft.Moyai.Tray." + identity.User!.Value, TrayInstance.Name);
    }

    [Fact]
    public void NormalExitAllowsLaterStartup()
    {
        string name = UniqueName();
        int calls = 0;
        Assert.True(TrayInstance.TryRun(() => calls++, name));
        Assert.True(TrayInstance.TryRun(() => calls++, name));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void StartupExceptionReleasesOwnership()
    {
        string name = UniqueName();
        Assert.Throws<InvalidOperationException>(() => TrayInstance.TryRun(() => throw new InvalidOperationException("fixture"), name));
        Assert.True(TrayInstance.TryRun(() => { }, name));
    }

    [Fact]
    public async Task ExistingInstanceSkipsAllStartupSideEffects()
    {
        string name = UniqueName();
        using var ready = new ManualResetEvent(false);
        using var release = new ManualResetEvent(false);
        Task<bool> first = Task.Factory.StartNew(() => TrayInstance.TryRun(() =>
        {
            ready.Set();
            Assert.True(release.WaitOne(TimeSpan.FromSeconds(10)));
        }, name), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(ready.WaitOne(TimeSpan.FromSeconds(5)));
            bool called = false;
            Assert.False(TrayInstance.TryRun(() => called = true, name));
            Assert.False(called);
        }
        finally { release.Set(); }
        Assert.True(await first);
        Assert.True(TrayInstance.TryRun(() => { }, name));
    }

    [Fact]
    public async Task IndependentInstanceNamesDoNotBlockEachOther()
    {
        string firstName = UniqueName();
        using var ready = new ManualResetEvent(false);
        using var release = new ManualResetEvent(false);
        Task<bool> first = Task.Factory.StartNew(() => TrayInstance.TryRun(() =>
        {
            ready.Set();
            Assert.True(release.WaitOne(TimeSpan.FromSeconds(10)));
        }, firstName), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(ready.WaitOne(TimeSpan.FromSeconds(5)));
            Assert.True(TrayInstance.TryRun(() => { }, UniqueName()));
        }
        finally { release.Set(); }
        Assert.True(await first);
    }

    [Fact]
    public void AbandonedOwnerAllowsRecovery()
    {
        string name = UniqueName();
        using var abandoned = new Mutex(false, name);
        var thread = new Thread(() => abandoned.WaitOne(0));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        // Joinの後もOSスレッドの終了と所有権の放棄処理が遅れる場合があるため、放棄の反映を待ってから回復を確認します。
        Assert.True(SpinWait.SpinUntil(() => TrayInstance.TryRun(() => { }, name), TimeSpan.FromSeconds(5)));
        Assert.True(TrayInstance.TryRun(() => { }, name));
    }

    private static string UniqueName() => @"Local\Moyai.Tray.Tests." + Guid.NewGuid().ToString("N");
}
