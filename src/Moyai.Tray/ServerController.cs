using System.Diagnostics;
using System.Text.Json;
using Moyai.Configuration;

namespace Moyai.Tray;

internal enum ServerState { Running, NotResponding, Error, Stopped }
internal sealed record ServerStatus(ServerState State, string? Version = null);

internal interface IServerController
{
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default);
    Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>Healthとインストール先のフルパスでユーザー別ホストを確認・制御します。</summary>
internal sealed class ServerController(string configPath, string serverPath) : IServerController
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 65536 };

    internal static Uri ResolveHealthUri(string path)
    {
        string serverUrl = new MoyaiSettings().ServerUrl;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("serverUrl", out var value) && value.ValueKind == JsonValueKind.String)
                serverUrl = value.GetString() ?? serverUrl;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { Program.LogError(exception); }
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
            uri = new Uri(new MoyaiSettings().ServerUrl);
        return new UriBuilder(uri.Scheme, uri.Host, uri.Port, "/health").Uri;
    }

    internal static ServerStatus ClassifyHealth(int statusCode, string body)
    {
        if (statusCode is < 200 or > 299) return new(ServerState.Error);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status)
                || status.ValueKind != JsonValueKind.String || status.GetString() != "ok") return new(ServerState.Error);
            string? version = root.TryGetProperty("version", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return new(ServerState.Running, version);
        }
        catch (JsonException) { return new(ServerState.Error); }
    }

    public async Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await Client.GetAsync(ResolveHealthUri(configPath), timeout.Token).ConfigureAwait(false);
            return ClassifyHealth((int)response.StatusCode, await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() => new ServerStatus(HasServerProcess() ? ServerState.NotResponding : ServerState.Stopped), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ServerStatus> StartAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var current = await GetStatusAsync(timeout.Token).ConfigureAwait(false);
            if (current.State == ServerState.Running) return current;
            await Task.Run(() =>
            {
                // 応答待ちの既存プロセスへ重ねて起動しません。
                if (HasServerProcess()) return;
                _ = MoyaiSettings.Load(configPath);
                var start = new ProcessStartInfo(serverPath) { UseShellExecute = false, CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(serverPath)! };
                start.ArgumentList.Add("--config");
                start.ArgumentList.Add(configPath);
                using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Moyai MCP server.");
            }, timeout.Token).ConfigureAwait(false);
            while (true)
            {
                var status = await GetStatusAsync(timeout.Token).ConfigureAwait(false);
                if (status.State == ServerState.Running) return status;
                await Task.Delay(500, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(ServerState.Error); }
    }

    public async Task<ServerStatus> StopAsync(CancellationToken cancellationToken = default)
    {
        // pipeはユーザー単位なので、同名の別インストールを止めないよう先にパスを確認します。
        if (!await Task.Run(HasServerProcess, cancellationToken).ConfigureAwait(false))
            return (await GetStatusAsync(cancellationToken).ConfigureAwait(false)).State == ServerState.Stopped
                ? new(ServerState.Stopped) : new(ServerState.Error);
        using (var graceful = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            graceful.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var owner = await UserHostControl.SendAsync("status", TimeSpan.FromMilliseconds(500), graceful.Token).ConfigureAwait(false);
                if (owner is not null && await Task.Run(() => IsServerProcess(owner.ProcessId), graceful.Token).ConfigureAwait(false))
                    await UserHostControl.SendAsync("stop", TimeSpan.FromSeconds(2), graceful.Token).ConfigureAwait(false);
                while (await Task.Run(HasServerProcess, graceful.Token).ConfigureAwait(false))
                    await Task.Delay(250, graceful.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or TimeoutException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Program.LogError(exception);
            }
        }
        using var force = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        force.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await Task.Run(async () =>
            {
                foreach (var process in FindServerProcesses())
                {
                    using (process)
                    {
                        try { process.Kill(); await process.WaitForExitAsync(force.Token).ConfigureAwait(false); }
                        catch (InvalidOperationException exception) { Program.LogError(exception); }
                    }
                }
            }, force.Token).ConfigureAwait(false);
            var status = await GetStatusAsync(force.Token).ConfigureAwait(false);
            return status.State == ServerState.Stopped && !await Task.Run(HasServerProcess, force.Token).ConfigureAwait(false)
                ? status : new(ServerState.Error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(ServerState.Error); }
    }

    private bool IsServerProcess(int id)
    {
        try { using var process = Process.GetProcessById(id); return MatchesPath(process); }
        catch (ArgumentException) { return false; }
    }

    private bool HasServerProcess()
    {
        var processes = FindServerProcesses();
        foreach (var process in processes) process.Dispose();
        return processes.Count != 0;
    }

    private List<Process> FindServerProcesses()
    {
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(serverPath)))
        {
            if (MatchesPath(process)) result.Add(process);
            else process.Dispose();
        }
        return result;
    }

    private bool MatchesPath(Process process)
    {
        try { return string.Equals(process.MainModule?.FileName, Path.GetFullPath(serverPath), StringComparison.OrdinalIgnoreCase); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Program.LogError(exception); return false; }
    }
}
