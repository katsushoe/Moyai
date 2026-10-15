# ADR 0013: Provider-owned local branch switching

## Status

Accepted for Consumer implementation on 2026-10-14. Development version1.3.12.0; real Provider acceptance and installation remain separate.

## Context

CR-2026-10-09-moyai-branch-switch requests switching an explicitly registered Project to an existing local branch. Githubie and Buckettie own Git execution, branch validation, policy and dirty-tree rejection. Their tool names and success payload nesting differ.

## Decision

Expose MCP `branch_switch(project, branch)` and symmetric CLI `branch-switch --project <name> --branch <name>` as destructive operations. Resolve Provider registration ID independently from Project identity, preserve signed UUID/canonical URL, and require `repository.branch.write` on the exact Provider tool. Legacy tokens are not accepted for this new operation.

Require explicit boolean capability `data.branch_switch` or Buckettie's `data.operations.branch_switch`. Missing, false, malformed or failed capability checks do not dispatch the mutation. Buckettie must also advertise `integration_mode=moyai`. Use `github_branch_switch` or `buckettie_branch_switch`, not `bitbucket_branch_switch`.

Forward only repository and literal branch arguments: no source, remote, fetch or branch creation. Provider controls clean-tree/branch existence/ref/policy validation. Preserve before/after branch data in existing raw Provider output (Githubie `data.previous_branch/current_branch`; Buckettie `data.branch_switch.previous_branch/current_branch/switched`), and preserve actionable branch rejection codes and Provider detail. Same-branch no-op follows Provider rules. Response loss after dispatch is unknown; inspect repository status before another switch rather than replaying automatically.

## Alternatives

Direct local Git: rejected because it bypasses Provider policy and audit. Switch via branch creation or remote checkout: rejected because it changes the requested operation. Treat a missing capability as support: rejected because older Providers must fail closed. A new shared result DTO is unnecessary because existing Provider output preserves each format.

## Impact and security

Application operation/scope/input validation, Infrastructure capability/tool/error/transport handling, MCP annotations and CLI help change; database schema and Project default branch do not. Enable the exact tool scope only in authorized environments. Do not change installed config or service during implementation.

## Verification and documentation

Tests cover Provider routes, signed UUID/URL/tool/scope, explicit/missing/malformed capabilities, mode/permission rejection, mapped registration IDs, unchanged omitted arguments, branch errors, unknown outcomes and CLI/MCP parity. CONFIG/COMMANDS and Provider contract describe authority and runtime prerequisites. Real clean/dirty/missing-branch tests use separately authorized disposable repositories; no live repository switch is part of unit verification.
