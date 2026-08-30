using System.Text.Json;
using Moyai.Application.Lifecycle;
using Moyai.Application.Projects;
using Moyai.Application.Providers;
using Moyai.Domain.Builds;
using Moyai.Domain.Events;
using Moyai.Domain.Projects;

namespace Moyai.Application.Builds;

/// <summary>Build実行と状態管理を提供します。</summary>
public sealed class BuildService(IProjectRepository projects, IBuildRepository builds, ProviderRoutingService repositoryProvider, LifecycleService lifecycle, TimeProvider timeProvider)
{
    /// <summary>CleanなSource CommitからBuildを開始し、結果を永続化します。</summary>
    public async Task<Build> StartAsync(string projectName, string configuration, string actorType, string actorName, CancellationToken cancellationToken = default)
    {
        Project project = await projects.GetRequiredAsync(projectName, cancellationToken).ConfigureAwait(false);
        RepositoryProviderResult status = await repositoryProvider.ExecuteAsync(projectName, RepositoryOperation.Status, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!status.Ok) throw new InvalidOperationException(status.ErrorMessage ?? "Repository status failed.");
        (string commit, bool dirty) = ParseStatus(status.Output);
        if (dirty) throw new InvalidOperationException("A standard build requires a clean working tree.");
        Build build = Build.Create(project.Id, project.BuildProvider, commit, configuration, project.BuildConfigJson, actorType, actorName, timeProvider);
        await builds.AddAsync(build, Event(build, "build_started", null), cancellationToken).ConfigureAwait(false);
        long revision = build.Revision;
        build.Start(timeProvider);
        await builds.UpdateAsync(build, revision, Event(build, "build_started", null), cancellationToken).ConfigureAwait(false);
        LifecycleResult result = await lifecycle.ExecuteAsync(projectName, LifecycleAction.Build, actorType, actorName, notes: project.BuildConfigJson, cancellationToken: cancellationToken).ConfigureAwait(false);
        revision = build.Revision;
        if (result.Ok) build.Succeed(timeProvider); else build.Fail(result.ErrorCode, result.ErrorMessage, timeProvider);
        await builds.UpdateAsync(build, revision, Event(build, result.Ok ? "build_succeeded" : "build_failed", result), cancellationToken).ConfigureAwait(false);
        if (result.Ok) await CollectArtifactsAsync(build, project.SourcePath, result.Output, cancellationToken).ConfigureAwait(false);
        return build;
    }

    public async Task<Build> GetAsync(string projectName, Guid buildId, CancellationToken cancellationToken = default) => await builds.GetAsync((await ProjectAsync(projectName, cancellationToken).ConfigureAwait(false)).Id, buildId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException($"Build '{buildId}' was not found.");
    public async Task<IReadOnlyList<Build>> ListAsync(string projectName, CancellationToken cancellationToken = default) => await builds.ListAsync((await ProjectAsync(projectName, cancellationToken).ConfigureAwait(false)).Id, cancellationToken).ConfigureAwait(false);
    public async Task<IReadOnlyList<BuildArtifact>> ListArtifactsAsync(string projectName, Guid buildId, CancellationToken cancellationToken = default) => await builds.ListArtifactsAsync((await ProjectAsync(projectName, cancellationToken).ConfigureAwait(false)).Id, buildId, cancellationToken).ConfigureAwait(false);
    public Task<LifecycleResult> CleanAsync(string projectName, string actorType, string actorName, CancellationToken cancellationToken = default) => lifecycle.ExecuteAsync(projectName, LifecycleAction.BuildClean, actorType, actorName, cancellationToken: cancellationToken);

    private async Task<Project> ProjectAsync(string name, CancellationToken token) => await projects.GetRequiredAsync(name, token).ConfigureAwait(false);
    private ProjectEvent Event(Build build, string type, object? value) => ProjectEvent.Create(build.ProjectId, "build", build.Id, type, build.ActorType, build.ActorName, null, JsonSerializer.Serialize(value ?? build), null, timeProvider);
    private async Task CollectArtifactsAsync(Build build, string sourcePath, string? output, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(output)) return;
        using JsonDocument document = JsonDocument.Parse(output);
        if (!document.RootElement.TryGetProperty("artifacts", out JsonElement artifacts) || artifacts.ValueKind != JsonValueKind.Array) return;
        foreach (JsonElement item in artifacts.EnumerateArray())
        {
            string name = item.GetProperty("name").GetString() ?? throw new InvalidOperationException("Artifact name is required.");
            string type = item.GetProperty("artifact_type").GetString() ?? throw new InvalidOperationException("Artifact type is required.");
            string relativePath = item.GetProperty("file_path").GetString() ?? throw new InvalidOperationException("Artifact path is required.");
            string sourceRoot = Path.GetFullPath(sourcePath);
            string fullPath = Path.GetFullPath(relativePath, sourceRoot);
            string sourcePrefix = sourceRoot.EndsWith(Path.DirectorySeparatorChar) ? sourceRoot : string.Concat(sourceRoot, Path.DirectorySeparatorChar);
            if (!fullPath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Artifact path must be inside the project source path.");
            BuildArtifact artifact = File.Exists(fullPath)
                ? new(Guid.NewGuid(), build.ProjectId, build.Id, name, type, "file", relativePath, new FileInfo(fullPath).Length, await HashFileAsync(fullPath, token).ConfigureAwait(false), null, timeProvider.GetUtcNow())
                : Directory.Exists(fullPath)
                    ? new(Guid.NewGuid(), build.ProjectId, build.Id, name, type, "directory", relativePath, null, null, await HashDirectoryAsync(fullPath, token).ConfigureAwait(false), timeProvider.GetUtcNow())
                    : throw new FileNotFoundException("Build artifact was not found.", fullPath);
            await builds.AddArtifactAsync(artifact, Event(build, "artifact_added", artifact), token).ConfigureAwait(false);
        }
    }
    private static async Task<string> HashFileAsync(string path, CancellationToken token) { await using FileStream stream = File.OpenRead(path); return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant(); }
    private static async Task<string> HashDirectoryAsync(string path, CancellationToken token)
    {
        var manifest = new System.Text.StringBuilder();
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)) manifest.Append(Path.GetRelativePath(path, file).Replace('\\', '/')).Append('\t').Append(await HashFileAsync(file, token).ConfigureAwait(false)).Append('\n');
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manifest.ToString()))).ToLowerInvariant();
    }
    private static (string Commit, bool Dirty) ParseStatus(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Repository status did not return source commit data.");
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement root = document.RootElement;
        string? commit = FindString(root, "commit") ?? FindString(root, "head_sha") ?? FindString(root, "head");
        bool dirty = FindBoolean(root, "dirty") ?? FindBoolean(root, "is_dirty") ?? false;
        return (!string.IsNullOrWhiteSpace(commit) ? commit : throw new InvalidOperationException("Repository status did not return a source commit."), dirty);
    }
    private static string? FindString(JsonElement value, string name) { if (value.ValueKind == JsonValueKind.Object) { if (value.TryGetProperty(name, out JsonElement found) && found.ValueKind == JsonValueKind.String) return found.GetString(); foreach (JsonProperty property in value.EnumerateObject()) { string? nested = FindString(property.Value, name); if (nested is not null) return nested; } } return null; }
    private static bool? FindBoolean(JsonElement value, string name) { if (value.ValueKind == JsonValueKind.Object) { if (value.TryGetProperty(name, out JsonElement found) && found.ValueKind is JsonValueKind.True or JsonValueKind.False) return found.GetBoolean(); foreach (JsonProperty property in value.EnumerateObject()) { bool? nested = FindBoolean(property.Value, name); if (nested is not null) return nested; } } return null; }
}
