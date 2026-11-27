using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Win32;
using Moyai.Configuration;

namespace Moyai.Cli;

/// <summary>User-profile initialization and logged-on host lifecycle operations.</summary>
public static class ServiceCommands
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Akatsukisoft.Moyai.Tray";
    private sealed record ProfileTransaction(string? OriginalAutostart, string? AppliedAutostart, string? CreatedConfigPath = null, string? CreatedConfigContent = null);

    public static void InitializeConfig(string path)
    {
        if (File.Exists(path)) { _ = MoyaiSettings.Load(path); return; }
        string fullPath = Path.GetFullPath(path);
        string dataDirectory = Path.Combine(MoyaiSettings.ProfileDirectory, "data");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        Directory.CreateDirectory(dataDirectory);
        WriteTextAtomically(fullPath, CreateInitialConfig(), overwrite: false);
    }

    public static async Task<object> ExecuteHostAsync(string command, string configPath)
    {
        if (command == "status")
        {
            HostControlResponse? status = await UserHostControl.SendAsync("status", TimeSpan.FromMilliseconds(500));
            return status is null ? new { ok = true, status = "not_running", processId = (int?)null } : status;
        }

        if (command == "stop")
        {
            HostControlResponse? result = await UserHostControl.SendAsync("stop", TimeSpan.FromSeconds(2));
            if (result is null) return new { ok = true, status = "not_running" };
            return result;
        }

        if (command == "start" || command == "restart")
        {
            _ = MoyaiSettings.Load(configPath);
            if (command == "restart")
            {
                HostControlResponse? stopped = await UserHostControl.SendAsync("stop", TimeSpan.FromSeconds(2));
                if (stopped is { Ok: false }) throw new InvalidOperationException("Moyai MCP host rejected the stop command. No replacement process was started.");
                if (stopped is not null) await WaitForStoppedProcessAsync(stopped.ProcessId);
                await HostControlWait.WaitAsync(
                    token => UserHostControl.SendAsync("status", TimeSpan.FromMilliseconds(300), token),
                    response => response is null, () => { }, TimeSpan.FromSeconds(15),
                    "Moyai MCP host did not stop within 15 seconds. No replacement process was started.");
            }
            HostControlResponse? running = await UserHostControl.SendAsync("status", TimeSpan.FromMilliseconds(500));
            if (running is not null) return new { ok = true, status = "already_running", running.ProcessId, running.ServerUrl };
            string executable = Path.Combine(AppContext.BaseDirectory, "host", "Moyai.Mcp.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("MCP host executable is missing.", executable);
            string logDirectory = MoyaiSettings.DefaultLogDirectory;
            Directory.CreateDirectory(logDirectory);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            start.ArgumentList.Add("--config");
            start.ArgumentList.Add(Path.GetFullPath(configPath));
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Moyai MCP host.");
            HostControlResponse? ready = await HostControlWait.WaitAsync(
                token => UserHostControl.SendAsync("status", TimeSpan.FromMilliseconds(300), token),
                response => response is { Ok: true, Status: "running" },
                () =>
                {
                    if (process.HasExited) throw new InvalidOperationException($"Moyai MCP host exited during startup with code {process.ExitCode}. Check the user logs in {logDirectory}.");
                }, TimeSpan.FromSeconds(60),
                "Moyai MCP host did not become ready within 60 seconds. Check host status before starting another process.");
            return new { ok = true, status = "running", ready!.ProcessId, ready.ServerUrl };
        }

        throw new ArgumentException("Unknown host command.");
    }

    internal static async Task WaitForStoppedProcessAsync(int processId, TimeSpan? budget = null)
    {
        Process process;
        try { process = Process.GetProcessById(processId); }
        catch (ArgumentException) { return; } // 停止応答後、観測前に終了したプロセスです。
        using (process)
        using (var timeout = new CancellationTokenSource(budget ?? TimeSpan.FromSeconds(15)))
        {
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException exception)
            {
                throw new TimeoutException("Moyai MCP host process did not exit within 15 seconds. No replacement process was started.", exception);
            }
        }
    }

    public static object SetAutostart(string command, string? trayExecutable = null)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException("Cannot open the current user's startup settings.");
        string tray = Path.GetFullPath(trayExecutable ?? Path.Combine(AppContext.BaseDirectory, "tray", "Moyai.Tray.exe"));
        if (command == "status")
        {
            string? value = key.GetValue(RunValue) as string;
            return new { ok = true, enabled = value is not null, command = value };
        }
        if (command == "enable")
        {
            if (!File.Exists(tray)) throw new FileNotFoundException("Moyai tray executable is missing.", tray);
            key.SetValue(RunValue, $"\"{tray}\" --autostart", RegistryValueKind.String);
            return new { ok = true, enabled = true };
        }
        if (command == "disable")
        {
            key.DeleteValue(RunValue, false);
            return new { ok = true, enabled = false };
        }
        throw new ArgumentException("Unknown autostart command.");
    }

    public static void RemoveProductAutostart(string transactionId, string? trayExecutable = null)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key is null) { SaveTransaction(transactionId, new(null, null)); return; }
        string? value = key.GetValue(RunValue) as string;
        string expected = $"\"{Path.GetFullPath(trayExecutable ?? Path.Combine(AppContext.BaseDirectory, "tray", "Moyai.Tray.exe"))}\"";
        bool owned = value?.StartsWith(expected, StringComparison.OrdinalIgnoreCase) == true;
        SaveTransaction(transactionId, new(value, owned ? null : value));
        if (owned) key.DeleteValue(RunValue, false);
    }

    public static void InitializeUserProfile(string configPath, string transactionId, string? trayExecutable = null)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true)
            ?? throw new InvalidOperationException("Cannot open the current user's startup settings.");
        string? original = key.GetValue(RunValue) as string;
        bool newProfile = !File.Exists(configPath);
        string tray = Path.GetFullPath(trayExecutable ?? Path.Combine(AppContext.BaseDirectory, "tray", "Moyai.Tray.exe"));
        string? applied = newProfile ? $"\"{tray}\" --autostart" : original;
        string fullConfigPath = Path.GetFullPath(configPath);
        string? initialConfig = newProfile ? CreateInitialConfig() : null;
        SaveTransaction(transactionId, new(original, applied, newProfile ? fullConfigPath : null, initialConfig));
        if (newProfile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullConfigPath)!);
            Directory.CreateDirectory(Path.Combine(MoyaiSettings.ProfileDirectory, "data"));
            WriteTextAtomically(fullConfigPath, initialConfig!, overwrite: false);
        }
        else _ = MoyaiSettings.Load(fullConfigPath);
        if (newProfile) _ = SetAutostart("enable", tray);
    }

    public static void FinishProfileTransaction(string phase, string transactionId)
    {
        string path = TransactionPath(transactionId);
        if (!File.Exists(path)) return;
        ProfileTransaction transaction = JsonSerializer.Deserialize<ProfileTransaction>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Invalid user-profile transaction record.");
        if (phase == "rollback")
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, true)
                ?? throw new InvalidOperationException("Cannot open the current user's startup settings.");
            string? current = key.GetValue(RunValue) as string;
            if (StringComparer.Ordinal.Equals(current, transaction.AppliedAutostart))
            {
                if (transaction.OriginalAutostart is null) key.DeleteValue(RunValue, false);
                else key.SetValue(RunValue, transaction.OriginalAutostart, RegistryValueKind.String);
            }
            if (transaction.CreatedConfigPath is { } createdPath && File.Exists(createdPath)
                && StringComparer.Ordinal.Equals(File.ReadAllText(createdPath), transaction.CreatedConfigContent))
                File.Delete(createdPath);
        }
        else if (phase != "commit") throw new ArgumentException("Profile transaction phase must be commit or rollback.");
        File.Delete(path);
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void SaveTransaction(string transactionId, ProfileTransaction transaction)
    {
        string path = TransactionPath(transactionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteTextAtomically(path, JsonSerializer.Serialize(transaction));
    }

    private static string TransactionPath(string transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '{' or '}')))
            throw new ArgumentException("Invalid profile transaction ID.");
        return Path.Combine(MoyaiSettings.ProfileDirectory, "transactions", transactionId + ".json");
    }

    private static string CreateInitialConfig()
    {
        var settings = new MoyaiSettings { ServerUrl = $"http://127.0.0.1:{GetAvailablePort()}", ApiToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) };
        settings.Validate();
        return JsonSerializer.Serialize(settings, MoyaiSettings.JsonOptions);
    }

    private static void WriteTextAtomically(string path, string contents, bool overwrite = true)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
