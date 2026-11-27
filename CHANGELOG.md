# Changelog

## Unreleased

## 2.1.0.3

- Fixed the installer failing with Windows Installer errors 2803/2867 when Moyai is running during an upgrade. The setup now shows a files-in-use dialog that can close the running Moyai programs automatically, and an error dialog.

## 2.1.0.2

- Githubie owner mode no longer compares the Githubie executable's SHA-256, so updating Githubie at the same path no longer stops Moyai with `discovery_runtime_mismatch`. Path, process image, owner, session, liveness, profile and ACL checks remain. `runtimeSha256` is deprecated and ignored (CR-2026-11-27).

- Added the MCP server tray submenu with four-state pill LEDs, Health checks, version display, bounded startup/shutdown, and a default-No stop confirmation warning about disconnecting all Codex/Claude Code sessions.

- Added the Moyai rope knot icon with a thick black outline, white fill, and transparent background to the tray executable, its windows, the notification area and the installed-programs list. Both light and dark taskbars use the same colors.

## 2.1.0.1

- Added an About dialog to the tray menu with the product name, tray version, publisher, and application description.

## 2.1.0.0

- Added a settings window to the Windows notification area for startup updates and Provider request timeout.
- Added automatic installation of signed stable MSI releases at tray startup while the MCP host is stopped.

## 2.0.4.4 - 2026-11-13 (development build)

- Clarify Windows-owner versus assertion authentication in settings design and PR tool metadata; preserve authentication and Provider policy behavior.
- Freeze the complete delivery/build input list, including resources, installer configuration and pinned tools, before packaging; verify file membership and hashes afterward. Git-state completeness remains a separate pre-commit gate.

## 2.0.4.3 - 2026-11-11 (development build)

- Accept the canonical `githubie` Provider name for settings GET and explicit Githubie settings updates, including the publication allowlist and routes, over the existing Windows owner authentication path. Preserve Provider binding, approval and refusal handling. Actual Provider acceptance remains separate.

## 2.0.4.2 - 2026-11-10 (development build)

- Preserve Githubie's PR publication source allowlist and PR routes in settings GET, explicit updates and changed fields across MCP/CLI. Distinguish omitted/empty arrays and validate typed values without importing local-commit permissions.
- Reject these Githubie-only settings for Buckettie before dispatch; retain existing authorization, Provider approval and unknown-result handling. Provider interoperability remains separate from this local compatibility correction.

## 2.0.4.1 - 2026-11-03 (development build)

- Prevent duplicate tray instances for the same Windows user/session before form creation or host/autostart actions. Release ownership on exit and recover after an abandoned owner. Close the legacy tray during the first upgrade to this guard.

## 2.0.4.0 - 2026-11-02 (development build)

- Add explicit PR source publication create/status Consumer operations and service-connected MCP/CLI, with required UUID/draft, dual capability/scope checks and Windows owner authentication.
- Preserve verified partial publication/PR results and request context; reject mismatched responses and never automatically replay unknown mutations. Existing PR creation and frozen owner-read v5 remain unchanged.

## 2.0.3.2 - 2026-11-02 (development build)

- Address the live tray acceptance failure where status could not be dismissed and restart stayed disabled. Display status and safe operation errors in a modeless window and restore commands as soon as the CLI operation finishes.
- Make status reachable from the taskbar. Close, Escape and the window close action hide only that window, preserving the tray and host without replaying any host operation.

## 2.0.3.1 - 2026-11-01 (development build)

- Fix installer error1943: resolve the persisted configuration-owner SID before the transaction and apply its permissions through the formatted WiX utility action instead of unsupported MSI properties in native SDDL.
- Include old-product removal in the major-upgrade transaction so an installation failure can restore the previous version. The2.0.3.0 live-install failure is retained as evidence; package-table verification alone does not establish installed acceptance.

## 2.0.3.0 - 2026-10-31 (development build)

- Add Githubie classic hosting branch-protection read/update/delete through common Provider operations, MCP and service-connected CLI, with strict partial edits and dedicated authenticated capability checks.
- Add schema8 durable operation audit independent of signing keys. Fail before dispatch when intent cannot be saved; preserve unknown outcomes and never automatically replay changes. Document Provider Desktop approval and classic/ruleset/gateway-policy distinctions. Actual Provider/GitHub/Desktop/MSI acceptance remains separate.

## 2.0.2.0 - 2026-10-30 (development build)

- Add secret-free loaded configuration and Provider authentication diagnostics, with byte-based configuration staleness and owner signature-not-required state.
- Add explicit tray host restart, readable English/Japanese status and configuration state, serialized CLI actions and no automatic replay/reload. Installed-host and GUI acceptance remain separate.


[English](CHANGELOG.md) | [日本語](CHANGELOG.ja.md)

## 2.0.1.0 - 2026-10-30 (development build)

- Add authenticated `pull_request_get` and SHA-pinned `pull_request_merge` through Githubie, with matching service-connected CLI commands.
- Require explicit capabilities and merge authority, compare an authenticated source snapshot before mutation, preserve typed pending/unknown outcomes and never replay a merge automatically. Buckettie PR state routing remains disabled; actual PR acceptance and installation are separate.

## 2.0.0.2 - 2026-10-25 (development build)

- Use only `<INSTALLFOLDER>\config\moyai.json` for operational configuration; reject alternative paths without user-profile fallback.
- Validate configuration before host start/restart and reserve creation for explicit initialization.
- Protect the central configuration for the configured Windows owner and retain that SID during MSI repair/upgrade. Preserve user-scoped DB, logs and signing keys; see ADR0022.

## 2.0.0.1 - 2026-10-25 (local installation)

- Separate host-control connection/response deadlines, avoid a second flush after stop ACK, and report incomplete control responses explicitly.
- Wait for the acknowledged host process to exit before restart; preserve caller cancellation and report readiness deadlines with status-check guidance.
- Accept basic tray status, stop/start and autostart disable/restore through user GUI actions with independent CLI correlation; the running host remains unchanged during autostart toggles.
- Encode the four-part product version into MSI ProductVersion2.0.1 for reliable upgrade ordering; see ADR0021. Local upgrade and MSI repair passed29 CLI checks each with existing profile retained; broader Windows acceptance and publication remain pending.

## 2.0.0.0 - 2026-10-22 (development build)

- Replace the machine-wide Windows Service host with a logged-on user host, per-user state, named-pipe controls and a tray controller.
- Preserve the legacy service database, configuration and key material for rollback; new user profiles start empty and require Project/Provider re-registration.
- Update MSI authoring to remove the service while retaining legacy state. Windows install/upgrade acceptance and public release remain pending.

## 1.3.14.0 - 2026-10-21 (local package)

- Add the `moyaimcp.exe` MCP command client alias, leading `--config`, and local `--version` while preserving `moyaictl` behavior.

## 1.3.13.1 - 2026-10-20 (local package)

- Preserve the assertion request guard when a `tools/call` payload is malformed.
- This entry describes a local installation package, not a public release.

## 1.3.12.0 - 2026-10-14 (development build)

- Add destructive branch-switch MCP/CLI routing to existing local branches through Githubie or Buckettie.
- Require explicit capability and Project-bound repository.branch.write; preserve branch errors and unknown outcomes without automatic replay.
- No installed configuration or live repository change; real Provider branch acceptance remains pending.

## 1.3.11.0 - 2026-10-14 (development build)

- Add typed settings get/partial-update MCP and CLI operations for the agreed Githubie contract; preserve omissions and explicit false/arrays.
- Require settings capabilities, Project assertions, dedicated write scope and Provider human approval. Unconfirmed Buckettie and legacy routes fail closed.
- Return eight non-secret registered settings. Unknown update outcomes are not retried automatically; read before another update.
- Not installed or publicly released; two-Provider integration remains pending.

## 1.3.10.0 - 2026-10-14 (development build)

- Store a nullable Provider Repository ID per Project in schema v7; unset mappings retain the legacy Project-name fallback without changing UUIDs or linked data.
- Add audited `project_provider_repository_set` / `project-provider-repository-set` for explicit mapping updates and clears using optimistic concurrency.
- Forward the registered ID to Repository and Repository-backed release tools while preserving Project labels and the signed UUID, canonical Repository URL, audience, and scopes.
- This entry describes a development build, not an installed or public release.

## 1.3.9.0 - 2026-10-08 (local package)

- Made `Project.GitRemoteName` optional; schema v6 rebuilds the `projects` table, converting stored `origin` to `null` (automatic resolution) and keeping other names as explicit values.
- Forward `gitRemoteName` as the Provider `remote` argument only when the Provider advertises `remote_resolution` version 1 / `repository_url`: for `repository_status` on Githubie and Buckettie, and for Buckettie `repository_commit`. Githubie commit and other operations do not receive `remote`.
- Preserve the Provider error codes `provider_remote_not_found`, `provider_remote_ambiguous` and `provider_remote_mismatch`.
- Build start now reads `local_head` as the source commit and treats `working_tree_clean=false` as dirty when `dirty`/`is_dirty` are absent.
- The isolated AC37 runtime (2026-10-07/08) ran this code while still labelled 1.3.8.0.
- This entry describes a local installation package, not a public release.

## 1.3.8.0 - 2026-09-27 (local package)

- Send per-tool Provider assertions for Buckettie release lookup, creation, publication and withdrawal instead of requiring the legacy static `release.write` service token.
- Added regression coverage for distinct validated Buckettie assertions on release lookup and creation.
- This entry describes a local installation package, not a public release.

## 1.3.7.0 - 2026-09-27 (local package)

- Re-check the Project ID, revision, repository, provider and deploy mode before retrying `auth_assertion_expired` in release lifecycle and KelpieSSH deployment, returning `auth_project_mismatch` instead of retrying when they changed.
- Made the legacy test-only `Es256AssertionValidator` internal; production validation uses the shared Moyai.ProviderAuthentication validator.
- This entry describes a local installation package, not a public release.

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
