# ADR 0012: Project-bound Repository settings Consumer

## Status

Accepted for Githubie and Buckettie implementation; Buckettie's October14 agreement was reconciled on October16. Both routes are implemented and unit-verified on October18. Real-Provider acceptance remains incomplete.

## Context

Provider policies and registered commit authors belong to the Provider. Moyai must support reading and partially updating them without guessing or replacing unrelated settings. Both Providers accepted the eight-field contract. Agreement and historical installation reports do not establish current runtime readiness.

## Decision

Add typed settings results and explicit changes, `repository_settings_get/update` MCP tools and symmetric CLI commands. Reuse Project routing, mapped Repository ID and Provider capability checks. Enable the agreed Githubie and Buckettie routes; reject other Providers. Githubie uses `github_repository_settings_get/update` and boolean `data.repository_settings_read/update`; Buckettie uses `bitbucket_repository_settings_get/update` and boolean `data.operations.repository_settings_read/update`. Both Buckettie reads and updates require `integration_mode=moyai`. Authentication follows ADR0023: verified Windows-owner mode uses the authenticated owner connection and no assertion or service token; assertion mode requires signed UUID/URL/tool context, with read scope `repository.read` and update scope `repository.settings.write`. Legacy token fallback is rejected in either case. Provider capability, policy and approval checks remain required, independently from commit/push and existing administrative tools. Successful typed results identify the selected Provider, not a hardcoded Githubie label.

Preserve omitted fields and explicit false/empty arrays. Reject unknown/null/invalid fields and incomplete author pairs. Provider validates its policy and obtains human approval before atomic persistence. Return only the eight common allowlisted settings, the optional Githubie extension below, and typed non-secret diagnostics. Provider approval must be true for update success. Transport loss after dispatch is an unknown outcome, never automatically retried; perform a separate read before the caller decides whether to submit another update.

## Githubie publication settings correction (2026-11-10)

The Kugutsu interoperability CR and Githubie2.1.5.0 introduce an independent publication-source allowlist AND exact PR routes. Exposing publication operations while dropping these settings on GET or rejecting their explicit updates prevents reviewing/configuring that policy. Extend the existing settings Consumer with optional `pr_publication_source_branches` (unique exact branch-name strings) and `pull_request_routes` (unique objects containing `source_branch` and `destination_branch`). Validate the Provider's settings wire shape, preserve case/order, distinguish omission from `[]`, reject explicit null/invalid values and return the corresponding successful `changed_fields` only with returned registered values. Route names follow the Provider settings input rule; publication-source names follow its stricter publication branch rule.

Older Githubie responses may omit either field independently; omit the corresponding optional output property rather than inventing defaults. Empty arrays remain visible. Buckettie rejects these update fields before dispatch and retains the eight common fields. Use existing settings capability/scope and Provider approval/persistence; no new default grants or automatic import of `local_commit_branches`. Review all retained entries before replacing an array. Changing JSON seed files alone does not update an existing Provider database. Downgrade compatibility and Provider backup/restore belong to the separately reviewed migration; Moyai does not migrate Provider data itself.

Alternatives rejected: treating an omitted field as empty loses compatibility information; silently ignoring new input makes a successful update misleading; auto-importing local-commit permissions changes the user's independent-publication policy. Update both MCP and connected CLI, typed input/output, documentation and focused input/result/Provider-boundary tests. Keep publication-source policy errors typed as `not_executed`, unknown results non-retryable, and actual Provider interoperability separate from local regression/candidate creation. The old candidate remains frozen; the corrected compatibility candidate is2.0.4.2.

## Githubie local-commit allowlist extension (2026-10-20, retained)

Owner-transport correction (2026-11-11): the canonical configured Provider name `githubie` must pass the same settings routing gates as the retained legacy name `githubbie`. Windows owner authentication continues through the existing verified owner discovery and capability checks, without requesting assertions or service tokens. This does not remap a Project bound to a different Provider name, grant any new Provider policy, or change approval/unknown-result behavior. Frozen2.0.4.2 fails this route before HTTP; the follow-up candidate is2.0.4.3. Regression must cover canonical-owner GET and explicit updates for all three Githubie extension fields, including mapped Repository ID and owner-mode flags.

Accept CR-2026-10-20-settings-local-commit-branches as a Githubie-only extension, not a change to Buckettie's eight-field agreement. `local_commit_branches` is an exact string-array replacement: omission preserves the registered value, `[]` explicitly clears it, and invalid types/null/blank elements are rejected. Preserve case, order and supplied existing entries; do not infer this list from push permissions or PR routes. The Provider remains responsible for branch policy and native approval.

Return Githubie's `local_commit_branches` only when supplied by the Provider; omit the optional output property when absent for older Provider compatibility. Present empty arrays remain `[]`, not absent/null. Accept this name in successful Githubie `changed_fields` only with its returned registered value. Reject its update for Buckettie before dispatch; keep ignoring unknown extra GET fields without making them editable. Existing `repository_settings_read/update` negotiation, signed Project/Repository/tool context and `repository.settings.write` remain unchanged. No new default grants, configuration writes or authentication fallback.

Tests cover exact/empty/omitted/invalid values, provider-specific output/changed fields, signed transport and refusal without the dedicated scope. CLI and MCP share the changes object; there is no separate administrative shortcut. A missing Settings tool in the installed `assertionToolScopes` fails before HTTP even if the Provider advertises the capability; distinguish that gate from a capability response that is missing/false. Candidate publication, installed configuration enablement and live approval remain separate gates. This extension does not authorize either Provider's registry mutation or commit/push delegation.

Alternatives: deriving commit permission from direct-push/PR routes or using an unsigned direct update is rejected because it changes the policy/authorization boundary; adding the field to Buckettie without a Provider agreement is rejected for compatibility. Optional Githubie output preserves the earlier eight-field response when the extension is absent.

## Original alternatives

- Existing all-fields administrative update: rejected because it crosses the administrative boundary and requires guessing unrelated settings.
- Persist Provider policy in Moyai: rejected because it creates a stale second source of truth.
- Infer support from tool publication or another Provider's capability layout: rejected; require the selected Provider's exact boolean capability.

## Impact and operation

No DB migration or installed configuration changes. Enable exact tools/scopes only after the Provider's corresponding runtime is available, with owner/user approval. Publish before isolated verification; shared Selftest DB and external push are outside the fixture.

## Verification and documentation

Test omission/false/arrays, invalid inputs, capability and authentication denial, mapping, approval cancellation, safe output, unknown transport outcomes and no retry. Test CLI JSON object conversion and MCP annotations/schema. Update CONFIG/COMMANDS and the public Provider contract. Installed and two-Provider acceptance are separate from implementation.
