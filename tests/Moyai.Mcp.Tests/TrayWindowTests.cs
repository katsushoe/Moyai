using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Moyai.Tray;

namespace Moyai.Mcp.Tests;

public sealed class TrayWindowTests
{
    private const string Running = "{\"Ok\":true,\"Status\":\"running\",\"ProcessId\":42}";
    private static readonly string[] AboutFieldNames = ["productName", "versionValue", "publisherValue"];
    private static readonly string[] ServerItemNames = ["serverStateItem", "serverCheckItem", "startItem", "stopItem"];

    [Fact]
    public void StopDeclineDoesNotSendAnyCommand() => RunOnSta(async () =>
    {
        int stops = 0;
        var controller = new FakeServer(action =>
        {
            if (action == "stop") stops++;
            return Task.FromResult(Running);
        });
        using var form = new MainForm(false, _ => Task.FromResult(Running), serverController: controller, confirmStop: () => false)
        { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-20000, -20000) };
        form.Show();
        await UntilAsync(() => !form.Visible);
        var server = Item(Menu(form), "serverItem");
        Assert.Same(server, Menu(form).Items[Menu(form).Items.Count - 3]);
        Assert.IsType<ToolStripMenuItem>(server.DropDownItems["stopItem"]).PerformClick();
        Assert.Equal(0, stops);
    });

    [Fact]
    public void PendingStartDisablesBothCommandsAndNeverDuplicates() => RunOnSta(async () =>
    {
        int starts = 0;
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new FakeServer(action =>
        {
            if (action == "start") { starts++; return response.Task; }
            return Task.FromResult("{\"ok\":true,\"status\":\"not_running\"}");
        });
        using var form = Fixture(_ => Task.FromResult(Running), controller);
        form.Show();
        await UntilAsync(() => !form.Visible);
        var server = Item(Menu(form), "serverItem");
        var start = Assert.IsType<ToolStripMenuItem>(server.DropDownItems["startItem"]);
        var stop = Assert.IsType<ToolStripMenuItem>(server.DropDownItems["stopItem"]);
        start.PerformClick();
        Assert.False(start.Enabled);
        Assert.False(stop.Enabled);
        Assert.Contains("...", server.DropDownItems["serverStateItem"]!.Text, StringComparison.Ordinal);
        start.PerformClick();
        Assert.Equal(1, starts);
        response.SetResult(Running);
        await UntilAsync(() => stop.Enabled);
        Assert.False(start.Enabled);
    });

    [Fact]
    public void ServerMenuTracksStartStopAndExternalStateChanges() => RunOnSta(async () =>
    {
        bool running = false;
        var calls = new List<string>();
        Task<string> Execute(string action)
        {
            calls.Add(action);
            if (action == "start") running = true;
            if (action == "stop") running = false;
            return Task.FromResult(running ? Running : "{\"ok\":true,\"status\":\"not_running\"}");
        }
        using var form = Fixture(Execute, new FakeServer(Execute));
        form.Show();
        await UntilAsync(() => !form.Visible);
        ContextMenuStrip menu = Menu(form);
        ToolStripMenuItem server = Item(menu, "serverItem");
        Assert.Contains("MCP", server.Text, StringComparison.Ordinal);
        Assert.Equal(ServerItemNames,
            server.DropDownItems.OfType<ToolStripMenuItem>().Select(item => item.Name));
        var state = Assert.IsType<ToolStripMenuItem>(server.DropDownItems["serverStateItem"]);
        var start = Assert.IsType<ToolStripMenuItem>(server.DropDownItems["startItem"]);
        var stop = Assert.IsType<ToolStripMenuItem>(server.DropDownItems["stopItem"]);
        Assert.True(start.Enabled);
        Assert.False(stop.Enabled);
        Assert.False(state.Enabled);
        Assert.Same(server.Image, state.Image);
        var stoppedColor = Assert.IsType<System.Drawing.Bitmap>(state.Image).GetPixel(8, 8);
        Assert.Equal(stoppedColor.R, stoppedColor.G);

        start.PerformClick();
        await UntilAsync(() => stop.Enabled);
        Assert.False(start.Enabled);
        var runningColor = Assert.IsType<System.Drawing.Bitmap>(state.Image).GetPixel(8, 8);
        Assert.True(runningColor.G > runningColor.R);
        start.PerformClick();
        Assert.Equal(1, calls.Count(action => action == "start"));
        stop.PerformClick();
        await UntilAsync(() => start.Enabled);
        Assert.False(stop.Enabled);
        Assert.Equal(1, calls.Count(action => action == "stop"));

        running = true;
        server.ShowDropDown();
        await UntilAsync(() => stop.Enabled);
        Assert.False(start.Enabled);
        server.HideDropDown();
    });

    [Theory]
    [InlineData("stopping")]
    [InlineData("unavailable")]
    public void ServerMenuAllowsRecoveryWhenStateIsNotConfirmed(string status) => RunOnSta(async () =>
    {
        var controller = new FakeServer(_ => Task.FromResult("{\"ok\":true,\"status\":\"" + status + "\"}"));
        using var form = Fixture(_ => Task.FromResult(Running), controller);
        form.Show();
        await UntilAsync(() => !form.Visible);
        var server = Item(Menu(form), "serverItem");
        Assert.True(server.DropDownItems["startItem"]!.Enabled);
        Assert.True(server.DropDownItems["stopItem"]!.Enabled);
    });

    [Fact]
    public void AboutMenuShowsProductVersionAndPublisherWithoutQueryingHost() => RunOnSta(async () =>
    {
        int hostCalls = 0;
        using var form = Fixture(_ => { hostCalls++; return Task.FromResult(Running); });
        form.Show();
        await UntilAsync(() => !form.Visible);
        int before = hostCalls;
        var observed = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        form.BeginInvoke(() =>
        {
            AboutForm? dialog = System.Windows.Forms.Application.OpenForms.OfType<AboutForm>().SingleOrDefault();
            if (dialog is null)
            {
                observed.SetException(new InvalidOperationException("The About menu did not open the dialog."));
                return;
            }
            try
            {
                observed.SetResult(AboutFieldNames
                    .Select(name => dialog.Controls.Find(name, true).Single().Text).ToArray());
            }
            finally { dialog.Close(); }
        });
        Item(Menu(form), "aboutItem").PerformClick();
        string[] fields = await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Moyai", fields[0]);
        Assert.Equal(typeof(AboutForm).Assembly.GetName().Version!.ToString(), fields[1]);
        Assert.Equal("Akatsukisoft", fields[2]);
        Assert.Equal(before, hostCalls);
        Assert.False(form.IsDisposed);
    });

    [Fact]
    public void StatusWindowAllowsRestartAndCanCloseWithoutExitingTray() => RunOnSta(async () =>
    {
        var calls = new List<string>();
        using var form = Fixture(action => { calls.Add(action); return Task.FromResult(Running); });
        form.Show();
        await UntilAsync(() => !form.Visible);
        ContextMenuStrip menu = Menu(form);

        Item(menu, "statusItem").PerformClick();
        await Task.Yield();

        Assert.True(form.Visible);
        Assert.True(form.ShowInTaskbar);
        Assert.True(Assert.IsType<TextBox>(form.Controls["statusDetails"]).ReadOnly);
        Assert.True(Item(menu, "restartItem").Enabled);
        Item(menu, "restartItem").PerformClick();
        await Task.Yield();
        Assert.Contains("restart", calls);
        Assert.True(form.Visible);
        Assert.True(Item(menu, "restartItem").Enabled);
        Assert.IsType<Button>(form.Controls["closeStatusButton"]).PerformClick();
        Assert.False(form.Visible);
        Assert.False(form.IsDisposed);
        Item(menu, "statusItem").PerformClick();
        await Task.Yield();
        form.Close();
        Assert.False(form.Visible);
        Assert.False(form.IsDisposed);
        Item(menu, "exitItem").PerformClick();
        Assert.True(form.IsDisposed);
    });

    [Fact]
    public void PendingStatusDisablesCommandsOnlyUntilResponseArrives() => RunOnSta(async () =>
    {
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var form = Fixture(_ =>
        {
            if (++calls == 1) return Task.FromResult(Running);
            Assert.IsType<WindowsFormsSynchronizationContext>(SynchronizationContext.Current);
            return response.Task;
        });
        form.Show();
        await UntilAsync(() => !form.Visible);
        ContextMenuStrip menu = Menu(form);

        Item(menu, "statusItem").PerformClick();
        Assert.False(Item(menu, "restartItem").Enabled);
        response.SetResult(Running);
        await UntilAsync(() => form.Visible && Item(menu, "restartItem").Enabled);

        Assert.True(form.ShowInTaskbar);
        Assert.IsType<Button>(form.Controls["closeStatusButton"]).PerformClick();
        Assert.False(form.Visible);
    });

    [Fact]
    public void FailedStatusShowsSafeModelessMessageAndRestoresCommands() => RunOnSta(async () =>
    {
        int calls = 0;
        using var form = Fixture(_ => ++calls == 1 ? Task.FromResult(Running) : Task.FromException<string>(new IOException("private-error-marker")));
        form.Show();
        await UntilAsync(() => !form.Visible);
        ContextMenuStrip menu = Menu(form);

        Item(menu, "statusItem").PerformClick();
        await Task.Yield();

        Assert.True(form.Visible);
        Assert.True(Item(menu, "restartItem").Enabled);
        Assert.DoesNotContain("private-error-marker", Assert.IsType<TextBox>(form.Controls["statusDetails"]).Text, StringComparison.Ordinal);
        form.Close();
        Assert.False(form.Visible);
        Assert.False(form.IsDisposed);
    });

    private static MainForm Fixture(Func<string, Task<string>> execute, IServerController? controller = null) =>
        new(false, execute, serverController: controller ?? new FakeServer(_ => Task.FromResult(Running)), confirmStop: () => true)
    {
        StartPosition = FormStartPosition.Manual,
        Location = new System.Drawing.Point(-20000, -20000)
    };

    private sealed class FakeServer(Func<string, Task<string>> execute) : IServerController
    {
        public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Run("status");
        public Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default) => Run("start");
        public Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default) => Run("stop");
        private async Task<ServerStatus> Run(string action)
        {
            var status = TrayHostStatus.Parse(await execute(action));
            return new(status.Status switch { "running" => ServerState.Running, "not_running" => ServerState.Stopped, _ => ServerState.Error });
        }
    }

    private static ContextMenuStrip Menu(MainForm form) => (ContextMenuStrip)typeof(MainForm)
        .GetField("trayMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static ToolStripMenuItem Item(ContextMenuStrip menu, string name) => Assert.IsType<ToolStripMenuItem>(menu.Items[name]);

    private static async Task UntilAsync(Func<bool> condition)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
        Assert.True(condition(), "The tray UI did not complete its state transition.");
    }

    private static void RunOnSta(Func<Task> action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            using var application = new ApplicationContext();
            ThreadExceptionEventHandler handler = (_, eventArgs) =>
            {
                error ??= ExceptionDispatchInfo.Capture(eventArgs.Exception);
                application.ExitThread();
            };
            async void Execute(object? sender, EventArgs eventArgs)
            {
                System.Windows.Forms.Application.Idle -= Execute;
                try { await action(); }
                catch (Exception exception) { error ??= ExceptionDispatchInfo.Capture(exception); }
                finally { application.ExitThread(); }
            }
            System.Windows.Forms.Application.ThreadException += handler;
            System.Windows.Forms.Application.Idle += Execute;
            try { System.Windows.Forms.Application.Run(application); }
            catch (Exception exception) { error ??= ExceptionDispatchInfo.Capture(exception); }
            finally
            {
                System.Windows.Forms.Application.Idle -= Execute;
                System.Windows.Forms.Application.ThreadException -= handler;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "The tray status UI blocked the STA message loop.");
        error?.Throw();
    }
}
