using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Moyai.Mcp.Tools;

/// <summary>設定や鍵を変更しない、稼働中ホストの診断入口です。</summary>
[McpServerToolType]
public sealed class DiagnosticTools(HostDiagnosticsService diagnostics)
{
    [McpServerTool(Name = "runtime_diagnostics", ReadOnly = true, Destructive = false, OpenWorld = false),
     Description("Reads the host's actual loaded config/DB paths, file staleness and nonsecret Provider/authentication mode metadata. Optional registered Project selection diagnoses missing/disabled repository Providers. Does not return tokens, fingerprints, keys or scope maps; does not contact Providers, sign, reload or restart.")]
    public Task<RuntimeDiagnosticsResult> RuntimeDiagnostics(string? project = null, CancellationToken cancellationToken = default) =>
        diagnostics.GetAsync(project, cancellationToken);
}
