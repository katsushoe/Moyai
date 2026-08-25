namespace Moyai.Domain.Projects;

/// <summary>Moyaiが管理するプロジェクトを表します。</summary>
public sealed class Project
{
    /// <summary>プロジェクトを生成します。</summary>
    public static Project Create(string name, string sourcePath, string? installPath, string repositoryUrl, string? repositoryProvider, string buildProvider, string deployMode, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(deployMode);
        ArgumentNullException.ThrowIfNull(timeProvider);

        string normalizedDeployMode = deployMode.ToLowerInvariant();
        if (normalizedDeployMode is not ("local" or "server")) throw new ArgumentException("Deploy mode must be 'local' or 'server'.", nameof(deployMode));
        if (normalizedDeployMode == "local" && string.IsNullOrWhiteSpace(installPath)) throw new ArgumentException("Install path is required for local deployment.", nameof(installPath));

        string provider = ResolveRepositoryProvider(repositoryUrl, repositoryProvider);
        DateTimeOffset now = timeProvider.GetUtcNow();
        return new Project(Guid.NewGuid(), name, sourcePath, installPath, repositoryUrl, provider, buildProvider, normalizedDeployMode, now);
    }

    private Project(Guid id, string name, string sourcePath, string? installPath, string repositoryUrl, string repositoryProvider, string buildProvider, string deployMode, DateTimeOffset now)
    {
        Id = id;
        Name = name;
        SourcePath = sourcePath;
        InstallPath = installPath;
        RepositoryUrl = repositoryUrl;
        RepositoryProvider = repositoryProvider;
        BuildProvider = buildProvider;
        DeployMode = deployMode;
        GitRemoteName = "origin";
        CreatedAt = now;
        UpdatedAt = now;
        Revision = 1;
    }

    /// <summary>永続化されたProjectを復元します。</summary>
    public static Project RestoreState(Guid id, string name, string? description, string sourcePath, string? installPath, string repositoryUrl, string repositoryProvider, string buildProvider, string? buildConfigJson, string deployMode, string? gitUserName, string? gitUserEmail, string gitRemoteName, string? gitDefaultBranch, DateTimeOffset createdAt, DateTimeOffset updatedAt, DateTimeOffset? archivedAt, long revision)
    {
        var project = new Project(id, name, sourcePath, installPath, repositoryUrl, repositoryProvider, buildProvider, deployMode, createdAt)
        {
            Description = description,
            BuildConfigJson = buildConfigJson,
            GitUserName = gitUserName,
            GitUserEmail = gitUserEmail,
            GitRemoteName = gitRemoteName,
            GitDefaultBranch = gitDefaultBranch,
            UpdatedAt = updatedAt,
            ArchivedAt = archivedAt,
            Revision = revision,
        };
        return project;
    }

    public Guid Id { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string SourcePath { get; }
    public string? InstallPath { get; }
    public string RepositoryUrl { get; }
    public string RepositoryProvider { get; }
    public string BuildProvider { get; }
    public string? BuildConfigJson { get; private set; }
    public string DeployMode { get; }
    public string? GitUserName { get; private set; }
    public string? GitUserEmail { get; private set; }
    public string GitRemoteName { get; private set; }
    public string? GitDefaultBranch { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public long Revision { get; private set; }

    /// <summary>Projectの編集可能な基本情報を更新します。</summary>
    public void Update(string name, string? description, string? buildConfigJson, string? gitUserName, string? gitUserEmail, string gitRemoteName, string? gitDefaultBranch, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitRemoteName);
        ArgumentNullException.ThrowIfNull(timeProvider);
        Name = name;
        Description = description;
        BuildConfigJson = buildConfigJson;
        GitUserName = gitUserName;
        GitUserEmail = gitUserEmail;
        GitRemoteName = gitRemoteName;
        GitDefaultBranch = gitDefaultBranch;
        UpdatedAt = timeProvider.GetUtcNow();
        Revision++;
    }

    /// <summary>プロジェクトをArchive状態にします。</summary>
    public void Archive(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArchivedAt = timeProvider.GetUtcNow();
        UpdatedAt = ArchivedAt.Value;
        Revision++;
    }

    /// <summary>プロジェクトをArchive状態から復元します。</summary>
    public void Restore(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArchivedAt = null;
        UpdatedAt = timeProvider.GetUtcNow();
        Revision++;
    }

    private static string ResolveRepositoryProvider(string repositoryUrl, string? explicitProvider)
    {
        if (!string.IsNullOrWhiteSpace(explicitProvider)) return explicitProvider.ToLowerInvariant();
        if (repositoryUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)) return "github";
        if (repositoryUrl.Contains("bitbucket.org", StringComparison.OrdinalIgnoreCase)) return "bitbucket";
        throw new ArgumentException("Repository provider is required when it cannot be inferred from the URL.", nameof(repositoryUrl));
    }
}
