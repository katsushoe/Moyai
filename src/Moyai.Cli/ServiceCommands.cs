using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;
using Moyai.Configuration;

namespace Moyai.Cli;

/// <summary>SCM lifecycle operations for the installed Moyai service.</summary>
public static class ServiceCommands
{
    public static void InitializeConfig(string path)
    {
        if (File.Exists(path)) { _ = MoyaiSettings.Load(path); return; }
        var settings = new MoyaiSettings();
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"Software\Akatsukisoft\Moyai");
        if (key?.GetValue("McpUrl") is string url) settings = settings with { ServerUrl = url };
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, MoyaiSettings.JsonOptions));
            File.Move(temporary, path, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static async Task<object> ExecuteAsync(string command, string configPath)
    {
        using var service = new ServiceController("Moyai");
        if (command == "register")
        {
            string executable = Path.Combine(AppContext.BaseDirectory, "Moyai.Mcp.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("MCP executable is missing.", executable);
            InitializeConfig(configPath);
            MoyaiSettings settings = MoyaiSettings.Load(configPath);
            GrantAccess(Path.GetDirectoryName(settings.DatabasePath)!, FileSystemRights.Modify);
            GrantAccess(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "logs")), FileSystemRights.Modify);
            GrantAccess(Path.GetDirectoryName(configPath)!, FileSystemRights.ReadAndExecute);
            await ScAsync("create", "Moyai", "binPath=", $"\"{executable}\" --config \"{configPath}\"", "start=", "auto", "obj=", @"NT AUTHORITY\LocalService", "DisplayName=", "Moyai MCP");
            return new { name = "Moyai", status = "registered" };
        }
        if (command == "status")
        {
            try { return new { name = "Moyai", status = service.Status.ToString(), startType = service.StartType.ToString(), canPause = service.CanPauseAndContinue }; }
            catch (InvalidOperationException exception) when (exception.InnerException is Win32Exception { NativeErrorCode: 1060 })
            { return new { name = "Moyai", status = "not_registered" }; }
        }
        if (command == "unregister")
        {
            if (service.Status != ServiceControllerStatus.Stopped) throw new InvalidOperationException("Stop Moyai before unregistering it.");
            await ScAsync("delete", "Moyai");
            return new { name = "Moyai", status = "unregistered" };
        }
        ServiceControllerStatus target = command switch
        {
            "start" or "resume" => ServiceControllerStatus.Running,
            "stop" => ServiceControllerStatus.Stopped,
            "pause" => ServiceControllerStatus.Paused,
            _ => throw new ArgumentException("Unknown service command."),
        };
        if (service.Status != target)
        {
            switch (command)
            {
                case "start": service.Start(); break;
                case "stop": service.Stop(); break;
                case "pause": service.Pause(); break;
                case "resume": service.Continue(); break;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            do { await Task.Delay(200, timeout.Token); service.Refresh(); } while (service.Status != target);
        }
        return new { name = "Moyai", status = service.Status.ToString() };
    }

    private static async Task ScAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start SCM client.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0) throw new InvalidOperationException($"SCM error {process.ExitCode}: {await output} {await error}");
    }

    private static void GrantAccess(string path, FileSystemRights rights)
    {
        DirectoryInfo directory = Directory.CreateDirectory(path);
        DirectorySecurity security = directory.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(security);
    }
}
