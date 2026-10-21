# ADR 0015: Explicit Provider-owned pull request creation

## Status

Accepted for Moyai Consumer implementation on 2026-10-18. Provider capability agreement/runtime rollout, isolated external acceptance, installation and release are pending separate gates.

## Context

Kugutsu needs PRs from explicitly permitted working branches, not a fixed develop-to-main route. Githubie/Buckettie route configuration belongs to their existing CRs. Moyai previously exposed no PR creation operation.

## Decision

MCP `pull_request_create(project, sourceBranch, destinationBranch, title, description, draft)` and service-connected CLI `pull-request-create` require all six inputs. Never infer branches or draft. Description may be empty but not null; title is nonblank, at most255 characters without control characters, description at most32768 without NUL. Branches are literal names, not HEAD, full refs or revision expressions. Do not decide whether a route is permitted inside Moyai.

Resolve the configured Provider, mapped registration ID and Project UUID/canonical URL through existing routing. Dedicated operations:

| Provider | Tool | Route arguments | Exact assertion scope | Explicit capability |
| :--- | :--- | :--- | :--- | :--- |
| Githubie | `github_pr_create` | `source_branch`, `destination_branch` | `provider.githubie.pull_request.write` | `data.pr_create=true` |
| Buckettie | `bitbucket_pr_create` | `sourceBranch`, `destinationBranch` | `provider.buckettie.pr_create` | `data.operations.pr_create=true` |

Both receive exactly repository, source/destination, title, description and draft. No credentials, local path, remote, push, merge or policy updates are arguments. Provider owns repository allowlist, allowed route/protection checks, any approval and upstream mutation. Require per-Project assertions on the exact tool; generic read/write/commit/push rights and legacy tokens never authorize PR creation. Buckettie additionally requires integration_mode=moyai. No direct or alternative-tool fallback.

The capability keys above are Consumer enablement conditions, not a claim that current Provider runtimes advertise them. Inspected Provider sources expose the tools but not those operation flags. Missing/false/malformed/failed capabilities must not dispatch creation; obtain owner agreement before enabling runtime scopes.

## Output and failure semantics

Return `PullRequestResult` with ok/operation/project/provider/pullRequest/error. Common pullRequest fields are id/url/title/description/sourceBranch/destinationBranch/draft/state. Map Githubie number/body/source/destination and Buckettie id/description/source_branch/destination_branch; retain Provider-reported state. Githubie currently omits draft in its model: report explicit null, not the request value or false. Absent URL/description remain null. Missing required result fields or invalid ID after success imply unknown outcome, not safe rejection.

Errors retain exact typed code, outcome and redacted Provider detail (including correlation/retryability if supplied). Unknown Project returns project_not_found; malformed inputs pull_request_invalid; absent capability provider_capability_missing; wrong scope auth_scope_denied; route denial remains pull_request_route_not_allowed. Lost/malformed replies after dispatch are unknown. Do not replay an unknown creation even if an inner code says expired/retryable; separately inspect Provider PR state before a human decides another attempt. Only known auth_assertion_expired rejection may use existing unchanged-context assertion retry.

## Alternatives

- Fixed develop/main defaults: rejected because they change explicit user intent.
- Generic repository.write or push scope: rejected because it expands privileges.
- Route-policy edits or raw remote APIs in Moyai: rejected because Provider owns that boundary.
- Return Provider-dependent success fields: rejected in favor of a small common typed result; preserve raw error detail after existing assertion redaction.

## Impact, security and operation

Extend RepositoryOperation/request, dedicated scope resolution, routing, Provider adapters/capability gates, response handling, MCP schema, CLI help and bilingual command/config docs together. No DB migration or installed settings/route-policy/account/key/ACL change. PR creation is externally visible: real acceptance requires an approved disposable target and allowed source/destination, not the production repository. Installed operation availability is not implied by source or publish success.

## Verification and delivery

Test both Providers' exact arguments/scopes and signed UUID/URL/repository mapping, capability absence/failure, wrong rights, legacy, Buckettie standalone mode, route denial, unknown/malformed response, common output and explicit null draft. Use the actual MCP schema for CLI argument tests. Preserve existing local-branch/settings work and regressions. Publish changed executables after passing checks; report remaining Provider agreements, runtime/installation and actual external PR acceptance separately. Result sharing requires authorized direct Itoguruma messages; do not change the original CR's source/target.
