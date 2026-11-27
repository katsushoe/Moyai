# ADR0026: Explicit PR source publication Consumer

## Status and context

Accepted for local implementation on2026-11-02. The agreed Consumer contract (2026-10-20, confirmed2026-10-24) maps Githubie's current2.1.4.0 publication contract. Earlier isolated snapshots referenced obsolete local_commit_branches; they are not applied. Installed normal host and frozen owner-read v5 remain separate from this development candidate2.0.4.0.

## Decision

Expose dedicated pull_request_publish_create/status through the common Application routing and service-connected MCP/CLI. Keep existing pull_request_create unchanged. Require explicit Project, source/destination, full source SHA, D-format operation UUID, title and draft boolean; description defaults to empty. Never derive a branch, SHA or UUID. Branch case is preserved; UUID/SHA are canonicalized to lower case.

Only Githubie is enabled. Creation requires strict true create and status capabilities, and in owner mode both advertised owner tools plus normal verified schema1 session/generation. Owner SID replaces assertion authorization; no signing key/grant is created. Legacy assertion mode requires both repository.push and provider.githubie.pull_request.write; status requires repository.read. Legacy service tokens cannot substitute. No Provider settings or ordinary push permissions are widened.

Provider owns protected-source/exact-route/clean-tree checks, Desktop approval, fixed-SHA creation-only publication, PR proof and durable operation journal. Moyai binds the registered Project/repository and sends only the dedicated arguments. Unknown publication/PR effects are retained separately. Same UUID is used in request context and assertion correlation. Provider same-ID reconciliation is not permission for Consumer automatic replay: neither operation retries automatically, including expired assertions.

Return a dedicated envelope with request_context, provider_data_available, data, error and requires_reconciliation. Keep validated partial data on failed responses, with both effect states, stage, observed SHA/time and PR evidence. Reject duplicate properties, conflicting MCP channels, wrong operation/UUID/route/SHA and unrecognized states. A malformed response after a possible mutation is unknown with no invented side-effect data. Output is limited to contract fields; no raw error/HTTP body is returned. Unknown uses retryable=false/check_status. CLI returns the same JSON and a nonzero failure exit.

## Alternatives and consequences

- Extend ordinary PR creation implicitly: rejected; publication must remain explicit.
- Automatically retry the same UUID or delete a branch after PR failure: rejected; reconciliation and explicit user decisions are required.
- Call raw Git/GitHub APIs from Moyai: rejected; retain Provider policy and approval boundary.
- Copy old Provider settings: rejected; current protected source and exact pull_request_routes apply.

Application owns validation/context/results, Infrastructure owns authenticated transport/capability gates, and MCP/CLI share schemas. Tests cover owner/assertion modes, both scopes/capabilities, pre-dispatch denial, request binding, partial results, invalid/unknown responses, no replay and interface parity. Bilingual operational docs describe recovery. Local tests/publish do not establish Provider/native-client/live acceptance, installed delivery or release. Frozen v5, normal host/config/DB and external repositories are untouched.

