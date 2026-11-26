using System.Text.Json;
using Moyai.Configuration;

namespace Moyai.Tray;

/// <summary>ホスト操作の多重実行を拒否し、自動再送をしません。</summary>
public sealed class TrayHostController(Func<string, Task<string>> execute)
{
    private int _busy;

    public async Task<string?> ExecuteAsync(string action)
    {
        if (action is not ("start" or "stop" or "restart" or "status")) throw new ArgumentException("Unsupported host action.", nameof(action));
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return null;
        try { return await execute(action).ConfigureAwait(false); }
        finally { Volatile.Write(ref _busy, 0); }
    }
}

/// <summary>CLIの状態を既知の項目だけへ投影し、生のJSONやエラーは表示しません。</summary>
public sealed record TrayHostStatus(string Status, int? ProcessId, HostConfigurationState? Configuration, DateTimeOffset? HostStartedAtUtc)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public static TrayHostStatus Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            // CLIと旧ホストでは命名規則が異なるため、大文字小文字を無視します。
            var response = root.Deserialize<StatusResponse>(JsonOptions);
            if (response?.Ok != true || response.Status is not ("running" or "not_running" or "stopping")) return new("unavailable", null, null, null);
            return new(response.Status, response.ProcessId, response.Configuration, response.HostStartedAtUtc);
        }
        catch (JsonException) { return new("unavailable", null, null, null); }
    }

    public string ConfigurationState => Status != "running" ? "unknown" : Configuration?.Status switch
    {
        "unchanged" when Configuration.RestartRequired == false => "unchanged",
        "restart_required" when Configuration.RestartRequired == true => "restart_required",
        "missing" => "missing",
        _ => "unavailable"
    };

    private sealed record StatusResponse(bool Ok, string Status, int? ProcessId, HostConfigurationState? Configuration, DateTimeOffset? HostStartedAtUtc);
}
