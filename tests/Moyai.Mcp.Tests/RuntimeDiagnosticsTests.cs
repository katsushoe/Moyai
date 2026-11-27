using System.Text.Json;
using Moyai.Application.Authentication;
using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using Moyai.Mcp.Tools;
using Moyai.Application.Projects;
using Moyai.Configuration;
using Moyai.Domain.Events;
using Moyai.Domain.Projects;
using Moyai.Tray;

namespace Moyai.Mcp.Tests;

public sealed class RuntimeDiagnosticsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "moyai-diagnostics-" + Guid.NewGuid().ToString("N"));
    private readonly string _token = new('A', 64);

    private (MoyaiSettings Settings, LoadedConfigurationSnapshot Snapshot, string Path) Load(params ProviderSettings[] providers)
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "moyai.json");
        var settings = new MoyaiSettings { ApiToken = _token, DatabasePath = "state.db", Providers = [.. providers] };
        File.WriteAllText(path, JsonSerializer.Serialize(settings, MoyaiSettings.JsonOptions));
        MoyaiSettings loaded = MoyaiSettings.Load(path, out LoadedConfigurationSnapshot snapshot);
        return (loaded, snapshot, path);
    }

    [Fact]
    public void ChangedBytesAreDetectedEvenWhenTimestampIsPreserved()
    {
        var (_, snapshot, path) = Load();
        DateTime time = File.GetLastWriteTimeUtc(path);
        File.AppendAllText(path, " ");
        File.SetLastWriteTimeUtc(path, time);
        Assert.True(snapshot.Read().RestartRequired);
        Assert.Equal("restart_required", snapshot.Read().Status);
    }

    [Fact]
    public void TimestampOnlyChangeDoesNotRequireRestart()
    {
        var (_, snapshot, path) = Load();
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.False(snapshot.Read().RestartRequired);
    }

    [Fact]
    public void MissingAndOversizedFilesAreUnknownRatherThanApplied()
    {
        var (_, snapshot, path) = Load();
        File.WriteAllBytes(path, new byte[4 * 1024 * 1024 + 1]);
        Assert.Equal("unavailable", snapshot.Read().Status);
        Assert.Null(snapshot.Read().RestartRequired);
        File.Delete(path);
        Assert.Equal("missing", snapshot.Read().Status);
        Assert.Null(snapshot.Read().RestartRequired);
    }

    [Fact]
    public void SnapshotDoesNotSerializeTokenOrFingerprint()
    {
        var (_, snapshot, _) = Load();
        Assert.Equal("{}", JsonSerializer.Serialize(snapshot));
        Assert.DoesNotContain(_token, JsonSerializer.Serialize(snapshot.Read()), StringComparison.Ordinal);
        Assert.Equal("x[REDACTED]y", snapshot.Redact("x" + _token.ToLowerInvariant() + "y"));
        Assert.Equal(Path.Combine(_directory, "state.db"), snapshot.Read().DatabasePath);
    }

    [Fact]
    public async Task OwnerDiagnosticSkipsSigningAndKeepsLoadedProviders()
    {
        ProviderSettings owner = new("githubie", null, "github", true)
        {
            AuthenticationMode = "windows_owner", ConnectionMode = "githubie_user_host_v1",
            RuntimeExecutable = Path.Combine(_directory, "Githubie.Server.exe"), RuntimeSha256 = new string('B', 64)
        };
        var (settings, snapshot, path) = Load(owner);
        var signing = new SigningProbe();
        var service = new HostDiagnosticsService(settings, snapshot, signing, new Projects(), TimeProvider.System);
        File.WriteAllText(path, "{}");
        RuntimeDiagnosticsResult result = await service.GetAsync();
        Assert.Equal("signature_not_required", Assert.Single(result.Providers).Authentication.State);
        Assert.False(result.Providers[0].SignatureRequired);
        Assert.False(result.ProviderContacted);
        Assert.Equal(0, signing.Calls);
        Assert.True(result.Configuration.RestartRequired);
    }

    [Fact]
    public async Task AssertionProjectionStripsEndpointSecretsAndProbesOnlyOnce()
    {
        var (settings, snapshot, _) = Load(new("one", "http://127.0.0.1:1234/private-token?token=secret", "one", true),
            new("two", "http://127.0.0.1:1235/" + _token, "two", false));
        var signing = new SigningProbe();
        RuntimeDiagnosticsResult result = await new HostDiagnosticsService(settings, snapshot, signing, new Projects(), TimeProvider.System).GetAsync("missing");
        Assert.Equal(1, signing.Calls);
        Assert.False(result.Ok);
        Assert.Equal("project_not_found", result.Error);
        Assert.Equal("http://127.0.0.1:1234", result.Providers[0].EndpointOrigin);
        string json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("private-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain(_token, json, StringComparison.Ordinal);
        Assert.All(result.Providers, provider => Assert.False(provider.Authentication.SigningAttempted));
    }

    [Theory]
    [InlineData(false, "provider_not_configured")]
    [InlineData(true, "provider_repository_disabled")]
    public async Task ProjectMismatchShowsActionWithoutChangingRouting(bool configured, string expected)
    {
        var (settings, snapshot, _) = Load(configured ? [new("githubbie", "http://127.0.0.1:1234/mcp", "github", false)] : []);
        Project project = Project.Create("fixture", _directory, null, "https://github.com/example/fixture", "github", "", "", TimeProvider.System);
        var result = await new HostDiagnosticsService(settings, snapshot, new SigningProbe(), new Projects(project), TimeProvider.System).GetAsync("fixture");
        Assert.Equal(expected, result.Project!.State);
        Assert.Equal("check_loaded_configuration_and_restart", result.Project.SuggestedAction);
    }

    [Theory]
    [InlineData("{\"ok\":true,\"status\":\"not_running\"}", "not_running", "unknown")]
    [InlineData("{\"Ok\":true,\"Status\":\"running\",\"ProcessId\":12}", "running", "unavailable")]
    [InlineData("{\"ok\":false,\"status\":\"running\",\"error\":\"secret\"}", "unavailable", "unknown")]
    [InlineData("[]", "unavailable", "unknown")]
    [InlineData("invalid secret", "unavailable", "unknown")]
    public void TrayTreatsOldOrMalformedStatusAsUnknown(string json, string status, string config)
    {
        TrayHostStatus result = TrayHostStatus.Parse(json);
        Assert.Equal(status, result.Status);
        Assert.Equal(config, result.ConfigurationState);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrayRejectsOverlappingLifecycleAndNeverRetriesFailure()
    {
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var controller = new TrayHostController(_ => { calls++; return finish.Task; });
        Task<string?> first = controller.ExecuteAsync("restart");
        Assert.Null(await controller.ExecuteAsync("stop"));
        finish.SetException(new IOException("fixture"));
        await Assert.ThrowsAsync<IOException>(() => first);
        Assert.Equal(1, calls);
        await Assert.ThrowsAsync<IOException>(() => controller.ExecuteAsync("status"));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void LockedFileIsUnknownAndUtf16LoadedBytesRemainComparable()
    {
        var (_, _, path) = Load();
        string json = File.ReadAllText(path);
        File.WriteAllText(path, json, System.Text.Encoding.Unicode);
        _ = MoyaiSettings.Load(path, out LoadedConfigurationSnapshot snapshot);
        Assert.False(snapshot.Read().RestartRequired);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal("unavailable", snapshot.Read().Status);
            Assert.Null(snapshot.Read().RestartRequired);
        }
        Assert.False(snapshot.Read().RestartRequired);
    }

    [Fact]
    public void DiagnosticToolSchemaIsLocalReadOnlyAndProjectIsOptional()
    {
        var tool = McpServerTool.Create(typeof(DiagnosticTools).GetMethod(nameof(DiagnosticTools.RuntimeDiagnostics))!,
            _ => throw new InvalidOperationException("Schema-only test."));
        Assert.Equal("runtime_diagnostics", tool.ProtocolTool.Name);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
        Assert.False(tool.ProtocolTool.Annotations.OpenWorldHint);
        Assert.True(tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("project", out _));
        if (tool.ProtocolTool.InputSchema.TryGetProperty("required", out var required)) Assert.Empty(required.EnumerateArray());
    }

    [Fact]
    public void TrayResourcesExplainRestartAndIndependentHostLifetimeInBothLanguages()
    {
        var resources = new ResourceManager("Moyai.Tray.MainForm", typeof(MainForm).Assembly);
        foreach (string culture in new[] { "en", "ja" })
        {
            Assert.NotEmpty(resources.GetString("RestartHost", CultureInfo.GetCultureInfo(culture))!);
            Assert.NotEmpty(resources.GetString("Config_restart_required", CultureInfo.GetCultureInfo(culture))!);
            Assert.Contains(culture == "ja" ? "ホストは継続" : "host keeps running", resources.GetString("ExitTray", CultureInfo.GetCultureInfo(culture))!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task PipeStatusReportsLoadedSnapshotAndChangedFileWithoutStoppingHost()
    {
        var (_, snapshot, path) = Load();
        string pipe = "moyai-diagnostic-" + Guid.NewGuid().ToString("N");
        using var lifetime = new Lifetime();
        using var service = new UserHostControlService(lifetime, "http://127.0.0.1:12345", NullLogger<UserHostControlService>.Instance, pipe, snapshot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await service.StartAsync(timeout.Token);
        try
        {
            File.AppendAllText(path, " ");
            HostControlResponse? status = await UserHostControl.SendAsync("status", TimeSpan.FromSeconds(2), pipe, timeout.Token);
            Assert.NotNull(status);
            Assert.True(status.Configuration!.RestartRequired);
            Assert.NotNull(status.HostStartedAtUtc);
            Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
            Assert.Equal("restart_required", TrayHostStatus.Parse(JsonSerializer.Serialize(status)).ConfigurationState);
        }
        finally { await service.StopAsync(timeout.Token); }
    }

    [Fact]
    public async Task MissingProjectDatabaseDoesNotCreateFileOrExposeRawException()
    {
        var (settings, snapshot, _) = Load();
        string database = Path.Combine(_directory, "missing.db");
        var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = database, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false };
        var projects = new Moyai.Infrastructure.Persistence.SqliteProjectRepository(new(connection.ToString()));
        var result = await new HostDiagnosticsService(settings, snapshot, new SigningProbe(), projects, TimeProvider.System).GetAsync("fixture");
        Assert.False(result.Ok);
        Assert.Equal("project_metadata_unavailable", result.Error);
        Assert.False(File.Exists(database));
        Assert.DoesNotContain("SQLite", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("missing.db", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private sealed class Lifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => new(true);
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopping.Token;
        public void StopApplication() => _stopping.Cancel();
        public void Dispose() => _stopping.Dispose();
    }

    private sealed class SigningProbe : ISigningReadiness
    {
        public int Calls { get; private set; }
        public Task<SigningReadiness> ReadAsync(CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new SigningReadiness("active_key_missing", false, false)); }
    }

    private sealed class Projects(Project? project = null) : IProjectRepository
    {
        public Task<Project?> GetByNameAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(project?.Name == name ? project : null);
        public Task<IReadOnlyList<Project>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Project>>(project is null ? [] : [project]);
        public Task AddAsync(Project value, ProjectEvent projectEvent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(Project value, long expectedRevision, ProjectEvent projectEvent, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
