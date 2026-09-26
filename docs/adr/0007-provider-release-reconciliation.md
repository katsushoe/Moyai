# ADR 0007: Reconcile existing Provider releases before retry

## Status

Accepted for implementation.

## Context

Moyai previously selected `release_create` and `release_publish` from its own failed workflow stage. A Provider release could be created or published independently after that failure. Retrying then produced `release_not_found` or `release_already_exists` even when the Provider held the intended release, leaving Moyai in `Failed`.

## Decision

- `release_publish` and `release_retry` query the configured Provider for the same version before creating a release.
- An absent Provider release is created and then published. An existing draft is published through its stable Provider identity when required. An existing published release becomes an idempotent success only after its version or tag, target commit, and artifacts match Moyai's recorded release.
- Githubie uses `github_release_get` and `github_tag_get`; Buckettie uses `buckettie_release_get` and `bitbucket_tag_get`. Provider-specific JSON is decoded in Infrastructure and exposed to Application as a common `AlreadyCompleted` result.
- Any mismatch returns `provider_conflict` with the differing fields. Provider lookup failures other than `release_not_found` remain failures. A create conflict caused by a lookup/create race triggers one fresh lookup rather than an unconditional duplicate create.

## Alternatives

- Infer the next call from Moyai's prior failure stage: rejected because Provider state can change independently.
- Treat every `release_already_exists` as success: rejected because a different commit or artifact could be accepted.
- Always publish an existing release again: rejected because a published matching release requires no Provider mutation.

## Impact

Release retries add read-only Provider calls before creation and, when a commit is recorded, a tag lookup. Matching published releases transition Moyai from `Publishing` to `Released` without another Provider mutation. `LifecycleResult` and `LifecycleRequest` carry reconciliation metadata; no database schema changes are required.

## Security conditions

Existing Provider authentication, repository allowlists, service-token scopes, and protected-operation policy remain authoritative. Reconciliation never falls back to another Provider and never suppresses lookup or comparison failures.

## Operational conditions

Operators must continue to approve the exact release retry. A conflict response lists the mismatched fields and requires the Provider or Moyai record to be corrected before retrying. Installed services require an updated build before this behavior is active.

## Implementation, tests and documentation

`ReleaseOrchestrationService` handles common idempotent completion. `McpLifecycleProvider` queries and validates Githubie and Buckettie releases. Tests cover absent, draft, matching published, artifact conflict, commit conflict, and persistence of Released or Failed state. `COMMANDS.md` and `COMMANDS.ja.md` describe the public retry behavior.
