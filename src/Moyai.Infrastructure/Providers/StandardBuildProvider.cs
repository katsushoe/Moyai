using System.Diagnostics;
using System.Text.Json;
using Moyai.Application.Lifecycle;

namespace Moyai.Infrastructure.Providers;

/// <summary>C#、Node、PHP向けの固定Command標準Build Providerです。</summary>
public sealed class StandardBuildProvider(string name) : ILifecycleProvider
{
    public string Name { get; } = name is "csharp" or "node" or "php" ? name : throw new ArgumentException("Unsupported standard build provider.", nameof(name));

    public async Task<LifecycleResult> ExecuteAsync(LifecycleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action is not (LifecycleAction.Build or LifecycleAction.BuildClean)) return new(false, "build", null, "operation_not_supported", "This provider only supports build operations.");
        (string fileName, IReadOnlyList<string> arguments) = Command(Name, request.Action, request.Notes);
        var startInfo = new ProcessStartInfo { FileName = fileName, WorkingDirectory = request.SourcePath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return new(false, "build", null, "build_failed", "Build process did not start.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string output = await stdout.ConfigureAwait(false);
            string error = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0) return new(false, "build", null, "build_failed", error);
            return new(true, request.Action == LifecycleAction.Build ? "build" : "build_clean", JsonSerializer.Serialize(new { output, artifacts = request.Action == LifecycleAction.Build ? Artifacts(request.Notes) : [] }), null, null);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            return new(false, "build", null, "build_provider_unavailable", exception.Message);
        }
    }

    private static (string FileName, IReadOnlyList<string> Arguments) Command(string name, LifecycleAction action, string? configJson)
    {
        string configuration = ReadString(configJson, "configuration") ?? "Release";
        return name switch
        {
            "csharp" when action == LifecycleAction.Build => ("dotnet", ["publish", "--configuration", configuration]),
            "csharp" => ("dotnet", ["clean", "--configuration", configuration]),
            "node" when action == LifecycleAction.Build => ("cmd.exe", ["/d", "/s", "/c", "npm.cmd", "run", "build", "--if-present"]),
            "node" => ("cmd.exe", ["/d", "/s", "/c", "npm.cmd", "run", "clean", "--if-present"]),
            "php" when action == LifecycleAction.Build => ("cmd.exe", ["/d", "/s", "/c", "composer.bat", "run-script", "build"]),
            "php" => ("cmd.exe", ["/d", "/s", "/c", "composer.bat", "run-script", "clean"]),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private static string? ReadString(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static object[] Artifacts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("artifacts", out JsonElement value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(static item => new { name = item.GetProperty("name").GetString(), artifact_type = item.GetProperty("artifact_type").GetString(), file_path = item.GetProperty("file_path").GetString() }).Cast<object>().ToArray();
    }
}
