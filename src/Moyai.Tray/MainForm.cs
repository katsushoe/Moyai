using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32;

namespace Moyai.Tray;

public partial class MainForm : Form
{
    private readonly System.ComponentModel.ComponentResourceManager _resources = new(typeof(MainForm));
    private readonly bool _startHostOnLoad;
    private readonly TrayHostController _host;
    private readonly bool _checkForUpdates;
    private static readonly HttpClient UpdateClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        MaxResponseContentBufferSize = 1024 * 1024
    };
    private bool _exiting;
    private bool _initialized;
    private bool _resourcesDisposed;
    private bool _hostControlsEnabled = true;
    private Icon? _trayIconImage;
    private bool? _trayIconForLightTaskbar;

    public MainForm(bool startHostOnLoad) : this(startHostOnLoad, null, checkForUpdates: true) { }

    internal MainForm(bool startHostOnLoad, Func<string, Task<string>>? execute, bool checkForUpdates = false,
        IServerController? serverController = null, Func<bool>? confirmStop = null)
    {
        _startHostOnLoad = startHostOnLoad;
        _checkForUpdates = checkForUpdates;
        _host = new TrayHostController(execute ?? RunCliAsync);
        string configPath = Moyai.Configuration.MoyaiSettings.DefaultPath;
        _serverController = serverController ?? new ServerController(configPath,
            Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(configPath))!, "bin", "host", "Moyai.Mcp.exe"));
        _confirmStop = confirmStop ?? ConfirmServerStop;
        InitializeComponent();
        UpdateServerState();
        TrayIcons.ApplyApplicationIcon(this);
        ApplyTrayIcon();
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        Disposed += (_, _) =>
        {
            if (_resourcesDisposed) return;
            _resourcesDisposed = true;
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            _trayIconImage?.Dispose();
            _serverLifetime.Cancel();
            _serverLifetime.Dispose();
            _statusLed.Dispose();
        };
        trayIcon.Visible = true;
        autostartItem.Checked = IsAutostartEnabled();
    }

    /// <summary>タスクバーの配色に合わせて通知領域アイコン（黒線または白線）を設定します。</summary>
    private void ApplyTrayIcon()
    {
        bool lightTaskbar = TrayIcons.IsLightTaskbar();
        if (_trayIconForLightTaskbar == lightTaskbar) return;
        Icon? previous = _trayIconImage;
        _trayIconImage = TrayIcons.LoadTray(lightTaskbar);
        _trayIconForLightTaskbar = lightTaskbar;
        trayIcon.Icon = _trayIconImage;
        previous?.Dispose();
    }

    private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // ライト/ダーク配色の切り替えは General カテゴリとして通知されます。
        if (e.Category != UserPreferenceCategory.General || IsDisposed) return;
        if (InvokeRequired)
        {
            if (IsHandleCreated) BeginInvoke(ApplyTrayIcon);
            return;
        }
        ApplyTrayIcon();
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        Hide();
        if (_checkForUpdates && await CheckForUpdatesAsync())
        {
            ExitItem_Click(this, EventArgs.Empty);
            return;
        }
        if (_startHostOnLoad) await RunServerOperationAsync(start: true, notify: false);
        else await RefreshServerStatusAsync(notify: false);
        await ExecuteAsync("status");
    }

    private async Task<bool> CheckForUpdatesAsync()
    {
        SetHostControls(false);
        try
        {
            if (!StartupUpdate.IsInstalled() || !Moyai.Configuration.MoyaiSettings.Load(Moyai.Configuration.MoyaiSettings.DefaultPath).AutoUpdateOnStartup)
                return false;
            if (!await IsHostStoppedAsync()) return false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            return await new StartupUpdate(UpdateClient).PrepareAsync(
                typeof(MainForm).Assembly.GetName().Version!, _startHostOnLoad, IsHostStoppedAsync, timeout.Token);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException
            or InvalidOperationException or System.Text.Json.JsonException or OperationCanceledException or KeyNotFoundException
            or System.ComponentModel.Win32Exception or FormatException)
        {
            Program.LogError(exception);
            return false;
        }
        finally { SetHostControls(true); }
    }

    private async Task<bool> IsHostStoppedAsync()
    {
        string? result = await _host.ExecuteAsync("status").ConfigureAwait(false);
        return TrayHostStatus.Parse(result ?? "{}").Status == "not_running";
    }

    private void SettingsItem_Click(object? sender, EventArgs e)
    {
        try
        {
            using var form = new SettingsForm(new SettingsFile(Moyai.Configuration.MoyaiSettings.DefaultPath));
            form.ShowDialog();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Program.LogError(exception);
            ShowStatusWindow(TextFor("SettingsUnavailable"));
        }
    }

    private void AboutItem_Click(object? sender, EventArgs e)
    {
        trayMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
        using var dialog = new AboutForm();
        dialog.ShowDialog();
    }

    private async void StartItem_Click(object? sender, EventArgs e) => await RunServerOperationAsync(start: true);

    private async void ServerItem_DropDownOpening(object? sender, EventArgs e)
    {
        await RefreshServerStatusAsync(notify: false);
    }

    private async void StopItem_Click(object? sender, EventArgs e) => await RunServerOperationAsync(start: false);

    private async void RestartItem_Click(object? sender, EventArgs e) => await ExecuteAsync("restart");

    private string TextFor(string key) => _resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    private void AutostartItem_Click(object? sender, EventArgs e)
    {
        string trayPath = Path.Combine(AppContext.BaseDirectory, "Moyai.Tray.exe");
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)
            ?? throw new InvalidOperationException(_resources.GetString("CannotOpenStartup", CultureInfo.CurrentUICulture) ?? "Cannot open logon startup settings.");
        if (autostartItem.Checked)
        {
            key.DeleteValue("Akatsukisoft.Moyai.Tray", false);
            autostartItem.Checked = false;
        }
        else
        {
            key.SetValue("Akatsukisoft.Moyai.Tray", $"\"{trayPath}\" --autostart", RegistryValueKind.String);
            autostartItem.Checked = true;
        }
    }

    private async void ShowStatusItem_Click(object? sender, EventArgs e)
    {
        await ExecuteAsync("status", showStatus: true);
    }

    private async Task ExecuteAsync(string action, bool showStatus = false, bool closeMenu = true)
    {
        if (!_hostControlsEnabled) return;
        if (closeMenu) trayMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
        SetHostControls(false);
        string? display = null;
        try
        {
            string? result = await _host.ExecuteAsync(action);
            if (result is null) return;
            if (action != "status") result = await _host.ExecuteAsync("status");
            TrayHostStatus status = TrayHostStatus.Parse(result ?? "{}");
            configurationItem.Text = TextFor("Config_" + status.ConfigurationState);
            if (showStatus || Visible) display = FormatStatus(status);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            configurationItem.Text = TextFor("Config_unknown");
            // CLI出力・例外詳細をダイアログへ流しません。期限切れの操作も再送しません。
            display = TextFor("HostOperationFailed");
        }
        finally { SetHostControls(true); }
        if (display is not null) ShowStatusWindow(display);
    }

    private void ShowStatusWindow(string text)
    {
        statusDetails.Text = text;
        Text = TextFor("HostStatusTitle");
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Show();
        Activate();
    }

    private void CloseStatus_Click(object? sender, EventArgs e) => HideStatusWindow();

    private void HideStatusWindow()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting || e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        HideStatusWindow();
    }

    private void SetHostControls(bool enabled)
    {
        _hostControlsEnabled = enabled;
        restartItem.Enabled = statusItem.Enabled = exitItem.Enabled = settingsItem.Enabled = enabled;
        UpdateServerState();
    }

    private string FormatStatus(TrayHostStatus status)
    {
        var lines = new List<string> { TextFor("Host_" + status.Status), TextFor("Config_" + status.ConfigurationState) };
        if (status.ProcessId is int processId) lines.Add("PID: " + processId.ToString(CultureInfo.InvariantCulture));
        if (status.HostStartedAtUtc is DateTimeOffset started) lines.Add(TextFor("HostStarted") + ": " + started.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        if (status.Configuration is { } config)
        {
            lines.Add(TextFor("ConfigPath") + ": " + config.ConfigPath);
            lines.Add(TextFor("DatabasePath") + ": " + config.DatabasePath);
            lines.Add(TextFor("ConfigLoaded") + ": " + config.LoadedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        }
        return string.Join(Environment.NewLine, lines);
    }

    private void ExitItem_Click(object? sender, EventArgs e)
    {
        _exiting = true;
        trayIcon.Visible = false;
        Close();
    }

    private async Task<string> RunCliAsync(string action)
    {
        string executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "moyaictl.exe"));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("host");
        start.ArgumentList.Add(action);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException(_resources.GetString("CannotStartCli", CultureInfo.CurrentUICulture) ?? "Could not start the Moyai CLI.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            // ホストの成否は不明なので、CLIだけを終了し、ホスト操作を再送しません。
            if (!process.HasExited) process.Kill();
            throw;
        }
        string error = await stderr;
        string output = await stdout;
        if (process.ExitCode != 0) throw new InvalidOperationException("Host CLI failed. Inspect host status before retrying.");
        return output;
    }

    private static bool IsAutostartEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("Akatsukisoft.Moyai.Tray") is string value && value.Contains("Moyai.Tray.exe", StringComparison.OrdinalIgnoreCase);
    }
}
