using Moyai.Application.Providers;

namespace Moyai.Application.Authentication;

/// <summary>ClientがScopeを選択できないRepository操作ポリシーです。</summary>
public static class RepositoryAssertionPolicy
{
    public static string Scope(RepositoryOperation operation) => operation switch
    {
        RepositoryOperation.ProviderVersion or RepositoryOperation.ProviderCapabilities or RepositoryOperation.Status
            or RepositoryOperation.Diff or RepositoryOperation.BranchList => "repository.read",
        RepositoryOperation.Commit => "repository.commit",
        RepositoryOperation.Push => "repository.push",
        RepositoryOperation.Pull => "repository.pull",
        RepositoryOperation.BranchCreate or RepositoryOperation.BranchDelete => "repository.branch.write",
        RepositoryOperation.TagCreate or RepositoryOperation.TagDelete or RepositoryOperation.TagPush => "repository.tag.write",
        _ => throw new ProviderAuthenticationException("auth_scope_denied"),
    };

    public static string NormalizeRepository(string repositoryUrl)
    {
        string value = repositoryUrl;
        if (value.StartsWith("git@", StringComparison.Ordinal)) value = "ssh://" + value[4..].Replace(':', '/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("https" or "ssh")
            || uri.Query.Length != 0 || uri.Fragment.Length != 0 || (uri.UserInfo.Length != 0 && uri.UserInfo != "git")
            || uri.AbsolutePath.Contains('%', StringComparison.Ordinal))
            throw new ProviderAuthenticationException("auth_project_mismatch");
        string path = uri.AbsolutePath.Trim('/');
        if (path.EndsWith(".git", StringComparison.Ordinal)) path = path[..^4];
        if (path.Length == 0) throw new ProviderAuthenticationException("auth_project_mismatch");
        return uri.IdnHost.ToLowerInvariant() + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "/" + path;
    }
}
