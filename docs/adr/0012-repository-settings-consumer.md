# ADR 0012: Project-bound Repository settings Consumer

## Status

Accepted for Githubie implementation, 2026-10-14. Buckettie agreement and live integration remain pending.

## Context

Provider policies and registered commit authors belong to the Provider. Moyai must support reading and partially updating them without guessing or replacing unrelated settings. Githubie accepted the proposed eight-field contract; its running version does not yet expose it.

## Decision

Add typed settings results and explicit changes, `repository_settings_get/update` MCP tools and symmetric CLI commands. Reuse Project routing, mapped Repository ID, signed UUID/URL/tool context, and Provider capability checks. Enable only the agreed Githubie route; reject other Providers before dispatch until their contracts are confirmed. Settings always require assertions, including read; no legacy token fallback. Read scope is `repository.read`; update is `repository.settings.write`, independently from commit/push and existing administrative tools.

Preserve omitted fields and explicit false/empty arrays. Reject unknown/null/invalid fields and incomplete author pairs. Provider validates its policy and obtains human approval before atomic persistence. Return only the eight allowlisted settings and typed non-secret diagnostics. Provider approval must be true for update success. Transport loss after dispatch is an unknown outcome, never automatically retried; perform a separate read before the caller decides whether to submit another update.

## Alternatives

- Existing all-fields administrative update: rejected because it crosses the administrative boundary and requires guessing unrelated settings.
- Persist Provider policy in Moyai: rejected because it creates a stale second source of truth.
- Wait for both Providers before unit implementation: rejected; implement the agreed route and fail closed on the unconfirmed route.

## Impact and operation

No DB migration or installed configuration changes. Enable exact tools/scopes only after the Provider's corresponding runtime is available, with owner/user approval. Publish before isolated verification; shared Selftest DB and external push are outside the fixture.

## Verification and documentation

Test omission/false/arrays, invalid inputs, capability and authentication denial, mapping, approval cancellation, safe output, unknown transport outcomes and no retry. Test CLI JSON object conversion and MCP annotations/schema. Update CONFIG/COMMANDS and the public Provider contract. Installed and two-Provider acceptance are separate from implementation.
