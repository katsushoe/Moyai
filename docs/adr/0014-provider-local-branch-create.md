# ADR 0014: Provider-owned local branch creation

## Status

Accepted for Moyai implementation on 2026-10-18. Provider capability rollout, dedicated acceptance, installation and release are separate pending gates.

## Context

Kugutsu requires a local proposal branch based on a release tag or explicit local source. Existing `branch_create` creates a remote branch; source forwarding alone cannot provide local creation. Repository mutation remains Provider-owned.

## Decision

Expose MCP `branch_create_local(project, branch, source)` and symmetric service-connected CLI `branch-create-local --project <name> --branch <name> --source <source>`. All three strings are required. Source is a literal local branch/tag or exactly40 hexadecimal characters interpreted as a commit SHA. Short hexadecimal names resolve only as literal branch/tag names, never abbreviated object IDs. Do not supply HEAD/default branches or accept revision expressions/fully qualified refs.

Githubie adapter uses `github_local_branch_create(repository, branch, source)` and requires boolean `data.local_branch_create=true` from a successful capability lookup. Exact Project-bound `repository.branch.write` authorization must cover that tool before contacting the Provider. Preserve mapped registration ID separately from signed UUID and canonical URL. Legacy tokens are rejected. Forward branch/source unchanged; remote, credentials and worktree paths are never tool arguments.

Buckettie has no confirmed dedicated tool/capability contract in the inspected source. Return `provider_capability_missing` without contacting it; do not guess a tool or dispatch remote `branch_create`. Enable another adapter only after a dedicated contract is confirmed and tested. Never execute local Git in Moyai.

Provider owns allowlist, source/ref resolution, policy and any required approval, repository locking and atomic mutation. Before changing refs reject dirty trees, unresolved conflicts, absent sources and existing target branches. Success adds only the new local branch ref: current branch/HEAD, commits, index/worktree contents, remote refs, repository/Project settings and upstream configuration remain unchanged. No checkout/fetch/push/remote access.

Use `RepositoryProviderResult`. `operation` is Moyai's `branch_create_local`; `output` retains complete successful Provider JSON including metadata (`name`, `source`, `current_branch`, `head_sha` for Githubie). Failure preserves exact typed Provider code and redacted detail, including outcome/retryability/correlation where supplied. Input errors use the existing configuration-error boundary.

Missing/false/malformed capability returns `provider_capability_missing` before mutation. Never bypass approval/auth/policy rejection. Response loss or malformed success after dispatch is `resultUnknown=true`. Never replay an unknown mutation; inspect local branch refs through Provider-supported reads before deciding another request. Assertion retry remains limited to a known `auth_assertion_expired` rejection with unchanged execution context.

## Alternatives

- Reuse remote branch creation: rejected because state changes and authorization target differ.
- Run Git in Moyai: rejected because it bypasses Provider ownership and policy.
- Dispatch merely because a tool exists: rejected because explicit capability rollout is required.

## Impact and security

Application operation/scope/routing validation, Infrastructure tool/capability/response handling, MCP schema, CLI help and bilingual command/configuration docs change together. No DB migration, runtime config edit, key/account/ACL change or default scope grant. Operator-approved enabling adds only the exact tool scope after Provider readiness.

## Validation and operation

Tests cover branch/tag/full SHA forwarding, invalid/missing inputs, mapped identity, scope/legacy rejection, explicit capability gating, no remote fallback, raw success/error retention and unknown response without replay. Actual MCP schema drives CLI symmetry. Mock transport tests do not prove filesystem invariants.

Acceptance requires an owner-provided allowlisted disposable fixture and explicit capability. Run source-branch/tag/SHA success and dirty/conflict/missing-source/duplicate rejection through Moyai; verify local/remote ref snapshots, HEAD/current branch, index/worktree and settings before/after, recording versions. Install/release and an authorized result reply are separate steps; source implementation does not close the CR.
