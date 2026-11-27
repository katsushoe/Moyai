# ADR0025: Hosting branch protection through Repository Providers

## Status and context

Accepted for local implementation on2026-10-31. CR-2026-10-27-branch-protection-provider requests read/update/delete of hosting protection with Provider approval and Moyai audit. Githubie's development2.1.2.0 contract is documented in its github-branch-protection specification. Installed Provider support and real GitHub/Desktop acceptance are separate and unconfirmed.

## Decision

Add common branch_protection_get/update/delete operations, explicit branch_protection_read/write capabilities, service-connected MCP/CLI entry points and typed nonsecret results. Enable the wire mapping for Githubie only. Require a registered Project/repository route, exact branch, owner authentication or Project-bound repository.read/dedicated provider.githubie.branch_protection.write assertions. Legacy tokens and general repository.write do not substitute. Owner operation advertisement, tools/list and ordinary capability flags must agree. Unconfirmed Providers remain disabled.

Nullable edit fields preserve existing settings. Match the Provider's eight explicit review/status/force/deletion/admin fields and validation rules. Do not convert a returned common policy projection into a complete PUT. Githubie owns full policy preservation, before/after Desktop approval, authenticated second read/concurrency rejection and GitHub permissions/plan checks. A no-op is not proof a dialog was shown. Rulesets are distinct from classic exact-branch protection; removing a classic rule does not prove a branch is unprotected. Githubie protected_branches is a separate gateway push policy.

Do not retry update/delete automatically, even for expired assertions. Typed Provider unknown/pending and transport loss after dispatch are non-success. Disagreeing or malformed result channels cannot establish success. Errors expose only safe code/outcome/action/correlation fields and a common policy projection; raw Provider JSON/messages and principal/credential data are excluded. A403 is access denied requiring permission and plan checks; do not invent a reliable plan/permission distinction.

Before a mutation route is dispatched, require a durable Moyai audit intent at the common Application entry, using the same resolved Project and operation ID as the actual request. Both typed and generic Application calls pass this gate. Record the final outcome separately, including a no-op or refusal. Use a new SQLite operation-audit table and migration, independent of signing keys. Retain Project UUID, canonical Provider/operation, a branch fingerprint, result code/outcome and time; never store request/policy bodies or credentials. Pending intent alone proves neither execution nor non-execution. Audit failure before dispatch fails closed; after dispatch it returns an explicit unknown audit/result error, preserving intent and requiring independent observation rather than replay. No client-selected audit path or authorization scope is added.

## Alternatives, security and impact

- Direct GitHub calls from Moyai: rejected; use the registered Provider and its approval/permission policy.
- Full policy replacement: rejected; explicit partial edits preserve supported unrelated settings and unsafe configurations are refused by the Provider.
- Reusing assertion-only audit for owner requests: rejected; owner mode requires no signing key, issuer or assertion context.
- Assume an unsupported Provider or absent capability is compatible: rejected; the feature remains unavailable.

Application defines input/result/audit contracts; Infrastructure implements wire/authentication and SQLite audit; MCP/CLI expose the same contract. Tests cover validation, capability and authority refusal before dispatch, exact mapping, approval no-op semantics, typed errors/unknown/non-replay, safe output/audit, migrations and schemas. Bilingual docs describe limits and explicit creation semantics. Local mocks and candidate publish do not close real Provider/Desktop/GitHub or installed-MSI acceptance. Existing normal configuration/grants/host and external repository protection are not changed by implementation.
