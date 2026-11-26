using System.Globalization;

namespace Moyai.Tray;

public partial class MainForm
{
    private readonly IServerController _serverController;
    private readonly Func<bool> _confirmStop;
    private readonly StatusLed _statusLed = new();
    private readonly CancellationTokenSource _serverLifetime = new();
    private ServerStatus? _serverStatus;
    private bool _serverBusy;
    private string? _serverOperation;

    private bool ConfirmServerStop() => MessageBox.Show(this, TextFor("ServerStopConfirm"), "Moyai",
        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private async void ServerCheckItem_Click(object? sender, EventArgs e) => await RefreshServerStatusAsync(notify: true);

    private async Task RefreshServerStatusAsync(bool notify)
    {
        if (_serverBusy || !_hostControlsEnabled || IsDisposed) return;
        _serverBusy = true;
        SetHostControls(false);
        try { _serverStatus = await _serverController.GetStatusAsync(_serverLifetime.Token); }
        catch (OperationCanceledException) when (_serverLifetime.IsCancellationRequested) { return; }
        catch (Exception exception) { Program.LogError(exception); _serverStatus = new(ServerState.Error); }
        finally
        {
            _serverBusy = false;
            if (!IsDisposed) SetHostControls(true);
        }
        if (notify && !IsDisposed) NotifyServerStatus(_serverStatus?.State == ServerState.Running);
    }

    private async Task RunServerOperationAsync(bool start, bool notify = true)
    {
        if (_serverBusy || !_hostControlsEnabled || IsDisposed) return;
        if (start && _serverStatus?.State == ServerState.Running) return;
        if (!start && _serverStatus?.State == ServerState.Stopped) return;
        trayMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
        if (!start && !_confirmStop()) return;
        _serverBusy = true;
        _serverOperation = TextFor(start ? "ServerStarting" : "ServerStopping");
        SetHostControls(false);
        try
        {
            _serverStatus = start ? await _serverController.StartAsync(_serverLifetime.Token)
                : await _serverController.StopAsync(_serverLifetime.Token);
        }
        catch (OperationCanceledException) when (_serverLifetime.IsCancellationRequested) { return; }
        catch (Exception exception) { Program.LogError(exception); _serverStatus = new(ServerState.Error); }
        finally
        {
            _serverOperation = null;
            _serverBusy = false;
            if (!IsDisposed) SetHostControls(true);
        }
        if (notify && !IsDisposed) NotifyServerStatus(_serverStatus?.State == (start ? ServerState.Running : ServerState.Stopped));
    }

    private string DescribeServerStatus()
    {
        if (_serverStatus is null) return TextFor("ServerUnknown");
        string text = TextFor("Server_" + _serverStatus.State);
        return _serverStatus.State == ServerState.Running && !string.IsNullOrEmpty(_serverStatus.Version)
            ? string.Format(CultureInfo.CurrentCulture, TextFor("ServerRunningVersion"), _serverStatus.Version) : text;
    }

    private void UpdateServerState()
    {
        serverStateItem.Text = _serverOperation ?? DescribeServerStatus();
        serverStateItem.Image = serverItem.Image = _statusLed.For(_serverStatus);
        serverStateItem.AccessibleDescription = serverStateItem.Text;
        serverItem.ToolTipText = serverStateItem.Text;
        bool enabled = _hostControlsEnabled && !_serverBusy;
        serverCheckItem.Enabled = enabled;
        startItem.Enabled = enabled && _serverStatus?.State != ServerState.Running;
        stopItem.Enabled = enabled && _serverStatus?.State != ServerState.Stopped;
    }

    private void NotifyServerStatus(bool success) => trayIcon.ShowBalloonTip(3000, "Moyai", DescribeServerStatus(),
        success ? ToolTipIcon.Info : ToolTipIcon.Warning);
}
