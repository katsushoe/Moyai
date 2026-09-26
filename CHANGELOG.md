# Changelog

[English](CHANGELOG.md) | [日本語](CHANGELOG.ja.md)

## Unreleased

## 1.3.6.0 - 2026-09-26 (local package)

- Call `get_version` and `*_provider_capabilities` without an assertion as bootstrap operations when the Provider capability does not list them, enabling Buckettie capability queries.
- Report Provider HTTP 401/403 authentication rejections as `provider_authentication_rejected` instead of `provider_unavailable`.
- Stop assertion-path HTTP clients from being cut off at the default 100 seconds instead of the configured request timeout.
- This entry describes a local installation package, not a public release.

## 1.3.5.0 - 2026-09-26 (local package)

- Reject release operations of non-assertion providers with `authentication_unavailable` outside the `mode=legacy` migration window instead of continuing with a static service token.
- Fix the broker key protector clearing the receive buffer before the response was read.
- This entry describes a local installation package, not a public release.

## 1.3.4.0 - 2026-09-25 (local package)

- Assertion-based lifecycle providers (Githubie releases) no longer require a static `release.write` service token, fixing release publication that still failed in 1.3.3.0 before the Provider was called.
- This entry describes a local installation package, not a public release.

## 1.3.3.0 - 2026-09-25 (local package)

- Attach a per-tool Provider assertion to Githubie release lookup, creation and publication (`github_release_get`, `github_tag_get`, `github_release_create`, `github_release_update`), fixing Moyai release publication against assertion-only Githubie.
- This entry describes a local installation package, not a public release.

## 1.3.2.0 - 2026-09-25 (local package)

- Before delegating a state-changing (non-read) operation to Buckettie, Moyai now checks `integration_mode` through the bootstrap `bitbucket_provider_capabilities` tool and rejects the operation with `provider_integration_mode_mismatch` unless it is `moyai`.
- This entry describes a local installation package, not a public release.

## 1.3.1.0 - 2026-09-24 (local package)

- Resolved the Provider assertion provider ID and audience to the canonical `githubie` ID while keeping the internal routing name `githubbie`.
- Included the shared Provider authentication package (Moyai.ProviderAuthentication 1.0.2) and the KelpieSSH Lifecycle Protocol v2 staged deployment adapter.
- This entry describes a local installation package, not a public release.

## 1.3.0.0 - 2026-09-07 (local package)

- Added per-operation ES256 Provider assertions, shared claim/replay validation, encrypted secret envelopes, signing-key and KEK rotation, and six service-backed MCP/CLI key-management operations.
- Added CNG, Keychain, Secret Service and authenticated broker key-protector adapters. Repository authentication now fails closed until configured; legacy operation requires an explicit migration window of at most seven days.
- Native macOS/Linux and real Provider migration/integration verification remain pending. This entry describes a local installation package, not a public release.

## 1.2.3 - 2026-09-04

- Changed `branch_create` to require an explicit literal branch or full commit SHA source, validate invalid revision expressions before Provider execution, and forward the source unchanged through the Repository Provider contract.
- Fixed release publication to create the Provider draft first, then pass the registered artifact and notes using the exact Githubie or Buckettie Provider argument contract.
- Fixed Lifecycle Provider business failures being reported as successful releases and added a reconciliation transition for previously misclassified releases.
- Changed `tag_create` to require and forward an explicit literal branch or full commit SHA source for Githubie.
- Send Githubie's required nullable annotated-tag message field during tag creation.

## 1.2.1 - 2026-09-04

- Fixed Repository Provider business failures being reported as success and CLI business failures returning exit code zero. Added structured/text response validation and regression coverage.

- Added user-scoped Codex/Claude Code configure and unconfigure commands, installer client/profile selection, owned-entry preservation and transactional rollback.

## 1.2.0 - 2026-09-03

- Updated the local installer to 1.2.0 and verified the installed service, CLI, configuration preservation, and unchanged counts across all 23 database tables.

- Add `project-rename` / `project_rename` to change only the Project name while retaining settings and related data, with revision checking and audit history.

- Allow name-only Project creation, idempotent registration through `project-ensure`, and later execution settings through `project-configure`. Validate required settings when Repository/build/deploy operations execute.

- Added installer-managed automatic Windows service startup under LocalService, with stop/remove lifecycle and existing database preservation.
- Replaced environment-based service configuration with persistent JSON and routed all business CLI commands through the service.
- Renamed the CLI to `moyaictl.exe` and added `service start`, `stop`, `pause`, `resume`, `register`, `unregister`, and `status` subcommands.
- Validated the 1.1.1 MSI upgrade, service pause/resume/stop/start, configuration preservation, and all 23 database table counts on the installed machine. For 1.2.0, all 17 isolated Windows VM checks passed, including service registration/unregistration, uninstall DB/configuration preservation, and reinstall data recovery.

## 1.0.7 - 2026-08-31

- Added MCP project discovery guidance, the `list_projects` tool alias, and registered-project candidates in not-found errors.
- Completed v1 acceptance traceability and expanded architecture, build, deployment, and release-content verification.

## 1.0.6 - 2026-08-30

- Added the Release domain model, SQLite persistence, application service, and matching CLI/MCP operations.
- Completed Repository Provider error normalization for retryable and policy failures.
- Aligned Project lookup, duplicate registration, and update targeting with ordinal case-insensitive comparison.

## 1.0.5 - 2026-08-29

- Repackaged version 1.0.4 without product behavior or database format changes.

## 1.0.4 - 2026-08-29

- Added SQLite schema v4 with relational constraints, revision contracts, and FTS5 synchronization.
- Added WorkItem relations, comments, Hataori task links, commit links, search, Project Overview, and Changes Since operations.
- Expanded the Repository Provider contract with capability, branch, and tag operations and normalized Provider errors.
- Added matching CLI and MCP operations, tests, and user documentation.
- Existing v1.0.3 databases migrate automatically with a backup created before migration.

## 1.0.3 - 2026-08-28

- Added repository URL and Provider updates through the Project update command and MCP tool.
- A repository URL change can infer its Provider again, while an explicit Provider overrides routing.
- Clarified that Moyai v1 manages one Repository association as part of each Project rather than through independent register/unregister commands.
- No database format changes from `1.0.2`.

## 1.0.2 - 2026-08-28

- Reorganized public documentation to follow the project document standard.
- Added paired English and Japanese entry, configuration, command, and MCP setup documents.
- Added package inventory, security policy, and canonical changelog documents.
- No product behavior or database format changes from `1.0.1`.

## 1.0.1 - 2026-08-28

- Added public user documentation.
- No product behavior or database format changes from `1.0.0`.

## 1.0.0 - 2026-08-28

- First stable release with CLI, Streamable HTTP MCP, SQLite state, provider routing, service authentication, and lifecycle operations.
