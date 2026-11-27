# ADR 0016: Capability-gated Buckettie Diff remote forwarding

## Status

Accepted for Moyai implementation,2026-10-18. Isolated live acceptance, installation and release are separate pending gates.

## Context

Project.gitRemoteName was not forwarded for Diff. Both Provider tools accept remote, but inspected Buckettie resolves it in its repository boundary, whereas Githubie's Diff path does not invoke its remote resolver. A published argument alone cannot prove validation. Existing Diff remains a local HEAD working-tree comparison, not a remote-branch comparison.

## Decision

Extend SupportsRemoteArgument only for bitbucket Diff, case-insensitive prefix matching. Reuse the existing gate: a nonblank Project remote plus a successful remote_resolution version1/mode repository_url capability lookup. Pass the exact configured value as remote alongside the mapped repository ID. Missing/empty/whitespace remote skips negotiation and omits the argument. Missing/malformed/failed/unsupported capability omits it under existing compatibility behavior; do not claim named-remote validation in those cases.

Preserve repository.read assertions, exact tool/Project UUID/URL binding, raw diff payload and typed Provider errors. No new scope, DB migration, config default or installed grant. Provider owns remote existence/URL validation. No origin fallback after a Provider rejection, local Git, fetch/push, diff rewriting or comparison-target change. Preserve existing Status, selected-commit and Buckettie commit behavior.

Githubie Diff stays excluded until its owner confirms named-remote validation conditions. This does not disable existing Githubie Diff; it still receives no remote argument.

## Alternatives

- Enable both Providers from tool schema alone: rejected because Githubie validation is unconfirmed.
- Resolve/validate Git remotes inside Moyai: rejected because Provider owns repository boundaries.
- Change failed capability lookup into a new error: rejected for this bounded extension; retain current compatibility semantics.

## Impact, verification and operation

Production changes are the bounded allowlist extension and a numeric-kind check before TryGetInt32, so malformed capability versions are unsupported rather than unhandled exceptions. Add unit/actual signed MCP transport regression, public contract and bilingual configuration/command/setup documentation. Tests cover explicit/blank remotes, exact capability shape and failure, mapped ID and assertion, wrong scope, raw Japanese/BOM/multiple-hunk/CRLF text and remote errors without replay. Publish affected MCP/CLI after verification. Real acceptance requires an owner-approved disposable Buckettie registration and a non-origin/missing/mismatched remote, with unchanged Git/Project state. No normal service, Provider config, shared DB or external connection change is part of implementation. Existing commit-policy rejection is not bypassed.
