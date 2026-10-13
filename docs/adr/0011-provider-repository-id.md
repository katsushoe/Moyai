# ADR 0011: Explicit Provider Repository IDs

## Status

Accepted for implementation, 2026-10-14. Live installation and Neon_SDK verification remain separate completion requirements.

## Context

Moyai Project names can differ from Githubie/Buckettie registration IDs. Using the Project name as the Provider `repository` argument prevents the Provider from resolving its registered Project binding. Renaming the Project would change its public identity rather than model this distinction.

## Decision

Store nullable `Project.ProviderRepositoryId` in schema v7 (`projects.provider_repository_id`). Existing rows remain null and use the Project name exactly as before. Preserve the explicit ID's case; never infer it from a path, URL, UUID, or Project rename.

Provide `project_provider_repository_set` and its CLI counterpart for setting/updating the mapping or clearing it with `clearMapping=true`. The dedicated operation avoids changing other Project fields and reuses optimistic concurrency, atomic persistence, and Project audit events. `project_get` and Project lists return the stored mapping. Existing create/configure/update/rename/archive operations preserve it.

IDs are 1–128 ASCII characters, start with a letter or digit, and contain only letters, digits, underscores, hyphens, or dots. Null is the stored unset state; empty/whitespace and URL/path-shaped inputs are rejected. An explicit clear restores the legacy name fallback.

Use the effective registration ID for Repository and Repository-backed lifecycle `repository` arguments. The Project name remains the Moyai project label. Assertion Project UUID, normalized Repository URL, provider audience, exact tool, scopes, and expiry handling remain unchanged. The Provider validates the supplied ID against its own registered UUID/URL binding. An incorrect mapping is not repaired through automatic registration or authentication fallback.

## Alternatives

- Rename the Project: rejected because it changes the client's identity and does not model Provider-specific registration.
- Infer registration IDs from URLs: rejected because the mapping is explicitly controlled by Provider registration.
- Replace Project names in all requests: rejected because Project labels and authentication identities are distinct from Provider IDs.

## Impact and operation

Schema v7 is additive; existing migration backup and transactional versioning remain in use. UUIDs, revisions, WorkItems, and relationships are retained. Clearing or changing a mapping is audited and requires the current Project revision. Provider registration and its management credentials are not changed by this operation. Provider authorization is checked when an operation is delegated, not asserted merely because the mapping was saved.

## Implementation and verification

Update the Domain/SQLite Project model, audited setter, MCP/CLI schemas and help, Provider requests and lifecycle forwarding, and CONFIG/COMMANDS documentation in both languages. Verify migration and restart persistence, unchanged UUID/links, clear/invalid/stale revisions, old Project fallback, exact outgoing IDs and unchanged signed context, and fail-closed wrong mappings. Publish the changed runtime before live registration and read-only `repository_status` verification; live commit/push is not part of this CR's acceptance.
