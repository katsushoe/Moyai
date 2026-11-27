# ADR 0018: Provider-owned PR read and merge

## Status

Implemented in local Moyai2.0.1.0 candidate on 2026-10-30 for the agreed Githubie contract. Actual Provider interoperability, installation and release remain outstanding. Common get/merge routing, authenticated SHA preflight, typed result parsing and MCP/service-connected CLI are connected. Buckettie PR state dispatch remains disabled following Provider discontinuation and does not block this implementation. Existing PR creation remains governed by ADR0015.

## Context

The PR merge Consumer CR and the Githubie/Buckettie Provider CRs dated 2026-10-21 request authenticated PR merging and read-based reconciliation. Direct connection read_only describes direct access, not Moyai-mediated write availability. Existing generic read/push/create rights do not establish merge authorization. PR #14 and release delivery are separate operations with their existing authorizations.

Githubie's owner reply80b8eaf286e84ab6af5c93f8323091ec (2026-10-22 JST, processed 2026-10-23) proposes exact get/merge scopes, capability flags and expected-SHA argument. Moyai accepts those proposed names and semantics below; Provider implementation/deployment and isolated acceptance are still required. Buckettie reports merge capability and its dedicated merge scope in the working tree, not deployed acceptance. Source-commit fields in proposed responses must not be mistaken for currently deployed schema support.

## Consumer decision and input

Expose the following API in the new candidate; installed older binaries do not acquire these commands automatically:

| MCP input | Service-connected CLI | Required fields |
| :--- | :--- | :--- |
| `pull_request_get` | `pull-request-get` | project, pullRequestId |
| `pull_request_merge` | `pull-request-merge` | project, pullRequestId, strategy, expectedSourceCommit; optional message |

`project` resolves the registered Project, UUID, canonical URL and mapped Provider repository ID. PR ID is a positive signed 64-bit integer. Strategy is an explicit case-sensitive common value: repository_default, merge, squash, rebase or fast_forward. Expected source is a full 40-character hexadecimal SHA for the current SHA-1 Provider contract; short SHA, refs, whitespace and revision expressions are rejected. Accept either hex case without changing the supplied value. Supporting another object format requires a separate contract change. Message omission/null means Provider default; empty/whitespace is rejected, and a nonblank value is preserved exactly. No implicit commit message or strategy is invented.

`PullRequestReadInput` and `PullRequestMergeInput` enforce only these common input constraints. Provider-specific strategy support, repository allowlists, source/destination route restrictions, draft/closed state, branch protection, reviews/checks and native approval remain Provider responsibilities.

## Provider contract checkpoint

| Item | Githubie | Buckettie |
| :--- | :--- | :--- |
| Existing tools | github_pr_get / github_pr_merge | bitbucket_pr_get / bitbucket_pr_merge |
| Read capability proposal | data.pr_get | data.operations.pr_get |
| Merge capability proposal | data.pr_merge | data.operations.pr_merge (owner reports source implementation) |
| Read assertion scope | repository.read (owner proposal accepted) | **Owner must confirm** |
| Merge assertion scope | provider.githubie.pull_request.merge (owner proposal accepted) | provider.buckettie.pr_merge (owner reported) |
| Source field proposal | source_commit_sha | source_commit_hash |
| Common merge strategy | merge (owner proposal accepted) | merge_commit (owner confirmed mapping) |
| Other strategy proposal | repository_default/squash/rebase; reject fast_forward | repository_default/squash/fast_forward; reject rebase |
| Expected SHA argument / upstream guarantee | expected_source_commit_sha; Provider proposes upstream PUT sha forwarding, not yet implemented | Existing Tool has no expected SHA; GET-to-POST race remains |

### Githubie proposal accepted on 2026-10-23

Require both exact boolean capability flags `data.pr_get` and `data.pr_merge` for merge. The proposed get fields are `data.source_commit_sha` (actual GitHub head) and nullable `data.draft`; preserve number/state/source/destination/merge_commit_sha. Reject Githubie PR IDs above2147483647 before any Provider dispatch, despite the wider common Int64 input. The exact Provider merge argument is `expected_source_commit_sha`, required by the Moyai contract even if optional for legacy Provider clients. Keep authenticated preflight and the proposed Provider source-SHA check; do not claim deployed upstream atomic checking from this agreement.

Accept repository_default→null merge_strategy, merge/squash/rebase, and pre-dispatch fast_forward rejection. Omitted/null message retains Provider defaults; nonblank message is preserved as commit_message (body appended to GitHub's automatic title, not an entire-title replacement). Accept an authenticated already-merged snapshot without a new PUT. Source mismatch, closed-unmerged, draft and policy rejection are not_executed; post-dispatch timeout/response loss/malformed response/follow-up read failure are unknown with retryable=false. Reconcile through a separate authenticated GET and never replay the mutation automatically. Preserve direct_connection=read_only and require the dedicated merge scope; agreement enables no installed scope or command.

The proposals must be explicitly accepted, including exact Tool argument names and updated get success/error examples. Capability presence alone cannot settle an unknown scope or response schema. Signed assertions must bind the exact read or merge tool, dedicated scope, Provider audience, registered Project UUID and canonical repository. Generic repository.read/write/push, PR-create scopes and legacy service tokens cannot substitute for an unconfirmed merge permission. Do not broaden existing installed settings or weaken direct read_only.

## State, output and failure semantics

`PullRequestStateResult` reserves operation/project/provider/ok/pullRequest/error. A PR snapshot includes id/url/state/sourceBranch/destinationBranch/sourceCommit/mergeCommit/draft. Preserve Provider-reported state and explicit null for unavailable draft; do not infer source/merge commits or copy request fields as response evidence. Provider-specific parsing is deliberately absent pending exact schemas.

The common error reserves code/outcome/retryable/suggestedAction/correlationId/ProviderDetail, after existing credential redaction. Proposed outcomes distinguish not_executed, failed, pending and unknown; exact owner result mapping is still required. Pending/unknown cannot be reported as completed. Unknown is never automatically replayed even if an inner Provider error says retryable. A malformed/lost success reply after dispatch is unknown. Read failure is not evidence that a merge did not execute.

Before mutation, perform separately authenticated PR read and compare its actual source commit to expectedSourceCommit. A mismatch rejects before dispatch. This does not make Buckettie's later POST atomic; document that guarantee boundary. Githubie's optional atomic upstream SHA check must not be claimed until the owner confirms and implements it. Already-merged handling requires authenticated snapshot/merge-commit reconciliation; it never sends a new merge merely because the requested PR is already merged. Automatic retries, including expired-assertion retries, must not replay an unknown mutation.

Buckettie reports async=true and at most three 250ms task polls; a pending task or outcome unknown requires authenticated PR read before deciding another operation. Preserve any returned operation/correlation identifier in redacted Provider detail. A matching final merged PR is observational evidence, not proof that a previous lost request caused that merge.

## Alternatives

### Local implementation checkpoint — 2026-10-30

The earlier Provider checkpoint table and proposal text preserve negotiation history. The current Githubie candidate contract is `Githubie/docs/specifications/pr-merge-provider.md` (candidate2.1.1.0). Exact arguments are repository, pull_request_number, merge_strategy, **message**, expected_source_commit_sha. The Provider passes message to upstream commit_message. repository_default maps to null; no strategy is guessed. Strict pr_get/pr_merge booleans, owner operations/tools-list membership or exact signed read/merge grants are required. No installed grant, key or configuration is edited.

Common merge requires an authenticated get and actual source SHA/PR identity. Source mismatch, unavailable draft metadata, draft or closed-unmerged state prevent dispatch. Already-merged observation requires a valid actual merge SHA and matching source SHA, without another mutation. Normal merge success must provide an actual merged snapshot matching the preflight PR identity, source SHA and branch route. Bare403, response loss, cancellation after dispatch, malformed success and unproven errors remain unknown; typed pending is preserved. Routing never retries merge, even on a known assertion-expired refusal; an operator must reconcile and start a new approved action. Read-only get remains separate. No real PR request or installed rollout is authorized by this ADR.

- Add dispatch using guessed scopes/fields: rejected because no shared authorization or result contract exists yet.
- Reuse push/create permission or bypass direct read_only: rejected because merge is a separate Provider-owned authority.
- Require atomic compare-and-merge from all Providers immediately: not selected; it would exclude Buckettie's reported API. The proposed baseline is mandatory preflight with an explicit race limitation, subject to owner agreement.
- Infer successful completion from mergeable=true or a pending task: rejected because protection checks and final merge state remain distinct.

## Impact and operation

After agreement, extend Application routing, operation/request contracts, exact assertion policy, capability/schema checks, authenticated get/preflight and Provider mapping together. Expose symmetric MCP/CLI operations only when those gates exist, with read-only versus destructive annotations and unknown-outcome handling. No database migration is expected. No installed trust, key, policy, scope, service or repository changes are performed in this preparation step.

## Verification and documentation plan

Verify input bounds/nullable message, Provider-specific strategy rejection, exact scopes and Project binding, absent/malformed capabilities, source mismatch before mutation, protected/draft/closed/already-merged states, async pending, transport cancellation, malformed success and reconciliation without replay. Exercise both MCP and service-connected CLI after wiring. Isolated actual PR acceptance requires owner-confirmed fixture, allowed route, deployed version and authorization; production PR #14 is not the fixture. Update bilingual COMMANDS/CONFIG/MCP_SETUP and release notes when operations become available. This preparation adds no public command documentation that would imply availability.
