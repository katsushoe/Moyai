using System.ComponentModel;
using ModelContextProtocol.Server;
using Moyai.Application.Authentication;

namespace Moyai.Mcp.Tools;

/// <summary>MCPと動的CLIに同じ公開鍵管理を提供します。</summary>
[McpServerToolType]
public sealed class AssertionTools(IAssertionAdministration administration)
{
    [McpServerTool(Name = "assertion_key_prepare"), Description("Explicitly prepares a next signing key. Returns only public trust metadata; distribute it to every Provider before activation.")]
    public Task<AssertionTrustKey> Prepare(CancellationToken cancellationToken = default) => administration.PrepareAsync(cancellationToken);

    [McpServerTool(Name = "assertion_key_get", ReadOnly = true), Description("Returns current public JWK, issuer, validity and signing-key state for trust distribution. Never returns private material.")]
    public Task<AssertionTrustKey?> Get(string kid, CancellationToken cancellationToken = default) => administration.GetAsync(kid, cancellationToken);

    [McpServerTool(Name = "assertion_key_activate", Destructive = true), Description("Activates a prepared key only after explicit confirmation that all Provider trust bundles have been updated.")]
    public async Task<object> Activate(string kid, bool trustDistributionConfirmed, int overlapSeconds = 86400, CancellationToken cancellationToken = default)
    {
        await administration.ActivateAsync(kid, overlapSeconds, trustDistributionConfirmed, cancellationToken).ConfigureAwait(false);
        return new { ok = true, key_id = kid };
    }

    [McpServerTool(Name = "assertion_key_revoke", Destructive = true), Description("Immediately revokes a signing key locally. Distribute the revoked public trust entry to every Provider before treating remote revocation as complete.")]
    public async Task<object> Revoke(string kid, CancellationToken cancellationToken = default)
    {
        await administration.RevokeAsync(kid, cancellationToken).ConfigureAwait(false);
        return new { ok = true, key_id = kid };
    }

    [McpServerTool(Name = "assertion_protector_rotate", Destructive = true), Description("Creates a new KEK in the configured protector and persists its public active version. Rewrap existing records after rotation.")]
    public async Task<object> Rotate(CancellationToken cancellationToken = default) => new { key_version = await administration.RotateProtectorAsync(cancellationToken).ConfigureAwait(false) };

    [McpServerTool(Name = "assertion_secret_rewrap", Destructive = true), Description("Authenticates existing envelopes and progressively rewraps their DEKs with the active KEK using optimistic concurrency.")]
    public async Task<object> Rewrap(CancellationToken cancellationToken = default) => new { count = await administration.RewrapAsync(cancellationToken).ConfigureAwait(false) };
}
