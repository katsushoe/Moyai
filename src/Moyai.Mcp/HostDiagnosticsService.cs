using Moyai.Application.Authentication;
using Moyai.Application.Projects;
using Moyai.Configuration;
using Moyai.Domain.Projects;

namespace Moyai.Mcp;

/// <summary>秘密値・権限辞書・discoveryを含まないProvider設定の投影です。</summary>
public sealed record RuntimeProviderDiagnostic(string Name, string ToolPrefix, string? EndpointOrigin,
    string ConnectionMode, string AuthenticationMode, bool Repository, bool SignatureRequired, SigningReadiness Authentication);

/// <summary>登録Projectの実際のProvider選択を診断します。</summary>
public sealed record ProjectProviderDiagnostic(string Project, string Provider, string State, string SuggestedAction);

/// <summary>稼働中ホストの読取専用診断です。接続/署名は試行しません。</summary>
public sealed record RuntimeDiagnosticsResult(bool Ok, string Operation, HostConfigurationState Configuration,
    IReadOnlyList<RuntimeProviderDiagnostic> Providers, ProjectProviderDiagnostic? Project, string? Error = null,
    bool ProviderContacted = false);

/// <summary>読み込み済み設定から安全な診断情報だけを構成します。</summary>
public sealed class HostDiagnosticsService(MoyaiSettings settings, LoadedConfigurationSnapshot snapshot,
    ISigningReadiness signing, IProjectRepository projects, TimeProvider clock)
{
    private readonly ProviderSettings[] _providers = settings.Providers.ToArray();

    public async Task<RuntimeDiagnosticsResult> GetAsync(string? project = null, CancellationToken cancellationToken = default)
    {
        SigningReadiness? assertion = null;
        var providers = new List<RuntimeProviderDiagnostic>();
        foreach (ProviderSettings provider in _providers)
        {
            bool required = provider.AuthenticationMode != "windows_owner" && settings.ProviderAuthentication.Mode == "assertion";
            SigningReadiness authentication;
            if (provider.AuthenticationMode == "windows_owner") authentication = new("signature_not_required");
            else if (settings.ProviderAuthentication.Mode == "legacy")
            {
                try
                {
                    _ = new RepositoryAuthentication("legacy", settings.ProviderAuthentication.LegacyStartedAt, settings.ProviderAuthentication.LegacyUntil).UseLegacy(clock);
                    authentication = new("legacy_token_not_inspected");
                }
                catch (ProviderAuthenticationException) { authentication = new("legacy_period_invalid"); }
            }
            else authentication = assertion ??= await signing.ReadAsync(cancellationToken).ConfigureAwait(false);
            providers.Add(new(snapshot.Redact(provider.Name), snapshot.Redact(provider.ToolPrefix), EndpointOrigin(provider.Endpoint),
                snapshot.Redact(provider.ConnectionMode), snapshot.Redact(provider.AuthenticationMode), provider.Repository, required, authentication));
        }
        ProjectProviderDiagnostic? selected = null;
        string? error = null;
        if (project is not null)
        {
            Project? registered = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(project)) registered = await projects.GetByNameAsync(project, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is System.Data.Common.DbException or IOException or UnauthorizedAccessException)
            { error = "project_metadata_unavailable"; }
            if (registered is null) error ??= "project_not_found";
            else
            {
                string name = registered.RepositoryProvider switch { "github" => "githubbie", "bitbucket" => "buckettie", _ => registered.RepositoryProvider };
                ProviderSettings? provider = _providers.SingleOrDefault(item => item.Name == name);
                string state = provider is null ? "provider_not_configured" : !provider.Repository ? "provider_repository_disabled" : "provider_configured_not_probed";
                selected = new(snapshot.Redact(registered.Name), snapshot.Redact(name), state,
                    state == "provider_configured_not_probed" ? "inspect_authentication_state" : "check_loaded_configuration_and_restart");
            }
        }
        return new(error is null, "runtime_diagnostics", snapshot.Read(), providers, selected, error);
    }

    private string? EndpointOrigin(string? endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return null;
        return snapshot.Redact(new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port).Uri.GetLeftPart(UriPartial.Authority));
    }
}
