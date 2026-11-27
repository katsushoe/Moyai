# Moyai configuration

## Tray settings and startup updates

Right-click **Moyai MCP** in the Windows notification area and select **Settings**. Automatic installation of updates is enabled by default and the preference takes effect at the next tray launch. The Provider request timeout accepts 1–3600 seconds and takes effect after restarting the MCP host. Both are saved in the canonical `moyai.json`.

The MSI-installed tray checks for a newer stable release on GitHub when the MCP host is stopped. The x64 MSI must match its published size and SHA-256 and have a valid Akatsukisoft Authenticode signature. Windows Installer may request administrator approval. The tray relaunches after the update attempt and records the result in `startup-update.log`.

## Githubie publication policy settings

Moyai2.0.4.2 forwards explicit `pr_publication_source_branches` and `pull_request_routes` updates through the existing Project-bound settings scope/capability; it adds no configuration grant. Provider values and human-approved persistence stay in Githubie. Both fields are Githubie-only, case-sensitive full-array replacements; routes contain `source_branch`/`destination_branch`. Omission preserves, empty clears, null/invalid/duplicate entries reject. GET does not invent omitted fields for older Providers. Source-list permission AND an exact route are needed for publication, with protected-source/default-deny checks enforced by Githubie. Never derive the new list from `local_commit_branches`, push permissions or seed JSON. Read current Provider settings and retain all required elements before an approved update; existing Provider DBs need an actual settings update, not a seed-file edit. No normal configuration or authentication mapping is changed by installing this Consumer correction. See [commands](COMMANDS.md) and [ADR0012](docs/adr/0012-repository-settings-consumer.md).

## Hosting protection authority

Development2.0.3.0 requires explicit Provider branch_protection_read/write capabilities. Owner authentication needs no signing key. Signed updates/deletions require provider.githubie.branch_protection.write; existing grants/configuration are not changed automatically. Moyai operation audit uses schema8. Provider protected_branches and GitHub classic protection/rulesets are distinct; see [hosting protection](docs/branch-protection.md).

## Loaded configuration diagnostics

Configuration changes apply after an explicit host restart. [Runtime diagnostics](docs/runtime-diagnostics.md) reports loaded paths and compares actual file contents without reloading them.


## PR read/merge authorization (development2.0.1.0)

No new top-level setting or database migration is required. In signed Githubie mode, the corresponding Provider's existing `assertionToolScopes` string-array dictionary requires exact entries `"github_pr_get": ["repository.read"]` and `"github_pr_merge": ["provider.githubie.pull_request.merge"]`. Default absence is denied; PR-create/write/push grants cannot authorize merge. Read uses the read grant and merge uses its separate grant, bound to the registered Project UUID, canonical repository and exact tool. Never enable or change installed grants automatically.

In the existing Githubie windows_owner configuration, no Issuer/signing key/legacy token is added. Require owner protocol1, verified discovery, both session/generation headers on every request, exact get/merge membership in owner operations and tools/list, and strict data.pr_get/data.pr_merge booleans (read alone needs only pr_get). A mode or metadata mismatch fails closed without static/token/signature fallback. Capability advertisement does not constitute real PR acceptance. Buckettie get/merge routing is disabled. Provider-owned GitHub credentials, allowlists, route/protection checks and approval remain authoritative. See [ADR0018](docs/adr/0018-provider-pull-request-merge.md).

## Provider user-host discovery (development)

Provider objects default to `connectionMode: "static"` with their existing loopback `endpoint`. Opt-in `buckettie_user_host_v1` requires `name: "buckettie"`, `toolPrefix: "bitbucket"`, an approved absolute `runtimeDirectory`, and canonical `expectedMcpPath` (default `/mcp`); omit `endpoint`. Dynamic failures never fall back to static configuration. Current Windows owner/session, private ACL, process generation and versioned record are verified before using the ephemeral owner credential on all HTTP. Signed operations retain ES256 authorization. Read reconnect is bounded to one retry; mutations are never automatically replayed. See [configuration, errors and operations](docs/provider-user-host.md) and [ADR 0020](docs/adr/0020-provider-user-host-consumer.md). This does not automatically re-enable Buckettie or update installed settings.

## Buckettie Diff remote selection (development)

An existing Project `gitRemoteName` is forwarded unchanged to Buckettie Diff only when nonblank and the Provider advertises `remote_resolution` version1/mode `repository_url`. Unset/blank values skip negotiation; absent/invalid/failed/unsupported capability omits remote for compatibility, not proof that an explicit name was validated. Githubie Diff still omits remote pending Provider validation confirmation. No new setting, default, DB migration or scope; reuse `repository.read`. No installed config/remote mutation. See [ADR0016](docs/adr/0016-buckettie-diff-remote.md).

## Pull request creation authorization (development)

After capability/contract readiness and operator approval, add exact `assertionToolScopes` entries `"github_pr_create": ["provider.githubie.pull_request.write"]` or `"bitbucket_pr_create": ["provider.buckettie.pr_create"]` to the corresponding Provider. These are string-array dictionary values; default is absent and denied. Existing read/commit/push authorization does not enable them. Require `data.pr_create=true` for Githubie, `data.operations.pr_create=true` and integration_mode=moyai for Buckettie. Explicit operation flags are pending owner agreement/rollout; tool availability alone is insufficient. No top-level setting, installed scope or Provider allowed-route policy is changed automatically. See [ADR0015](docs/adr/0015-provider-pull-request-create.md).

## Local-branch creation authorization (development)

After Githubie advertises `data.local_branch_create=true`, an administrator may add `github_local_branch_create` to its `assertionToolScopes` dictionary with exact string-array value `["repository.branch.write"]`. Entry example: `"github_local_branch_create": ["repository.branch.write"]`. Default: absent and denied. Generic write/remote branch authorization does not enable this tool. No new top-level keys; implementation never edits installed scopes, keys, accounts or DB. Buckettie has no confirmed local-create mapping and remains unsupported. See [ADR0014](docs/adr/0014-provider-local-branch-create.md).

## Branch-switch authorization (development1.3.12.0)

After the Provider runtime explicitly advertises branch_switch and the administrator authorizes enablement, configure github_branch_switch: [repository.branch.write] or buckettie_branch_switch: [repository.branch.write] in the respective assertionToolScopes. Buckettie's configured prefix remains bitbucket; only this operation resolves to buckettie_branch_switch. Require Moyai integration mode for Buckettie. Implementation does not edit installed scopes, signing keys, DB or service account. See [ADR0013](docs/adr/0013-provider-branch-switch.md).

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

## Same-user Provider authentication

For a Buckettie Provider with `connectionMode: "buckettie_user_host_v1"`, explicitly set `authenticationMode: "windows_owner"` to use verified Windows-owner authentication. The default remains `"assertion"`. Owner mode requires Buckettie2.0.1.0 or later, contract3 and its explicit owner-protocol1 capability. Set `runtimeDirectory` to the approved private user-host runtime. Static endpoints and Githubie owner mode are rejected until their supported transport contract is implemented.

Owner Repository and release calls use `X-Buckettie-Session` on every HTTP request and omit Authorization, Issuer/signing services and legacy service tokens. No fallback occurs on missing capability or invalid discovery. Provider Repository/branch policy and approval remain enforced; Project mapping is retained for routing. Mutations with uncertain outcomes are never automatically replayed. This source implementation has not changed the installed configuration; dedicated interoperability and a backed-up rollout remain required. See [ADR0023](docs/adr/0023-same-user-provider-trust.md).

MCP client registration uses the current Windows user's profile and preserves unrelated client settings. MSI properties: `MOYAI_CODEX`, `MOYAI_CLAUDE` (select with `1`), `MOYAI_CLIENT_PROFILE` (the current user's absolute profile path). See [MCP setup](MCP_SETUP.md#installer-client-registration) for ownership, uninstall and recovery behavior.

## Configuration file

`<INSTALLFOLDER>` is the installation directory selected in MSI, or passed as `INSTALLFOLDER` for a silent install. It is retained for repair/upgrade. The runtime reads its 64-bit HKLM product registration; unregistered packages resolve their binary layout. No drive or installation directory is hardcoded. This is installation metadata, not an alternate configuration file.

Repository authentication defaults to ES256 assertions and fails closed until configured. See [Provider authentication settings, key initialization and migration](docs/specifications/provider-authentication-operations.md). Static repository tokens require an explicitly bounded migration window of at most seven days.

The host, CLI and tray use only `<INSTALLFOLDER>\config\moyai.json`. `--config` accepts only a normalized reference to that same file; other paths are rejected. Missing or invalid configuration fails explicitly, with no user-profile or environment fallback. Host start/restart validates the file before changing processes. The CLI connects to the configured host and never opens the database. Restart the host after changes. See [ADR0022](docs/adr/0022-single-operational-configuration.md).

```json
{
  "databasePath": "<user-profile>\\data\\moyai.db",
  "serverUrl": "http://127.0.0.1:<persisted-user-port>",
  "apiToken": "<64-hex-character-secret-generated-by-config-init>",
  "requestTimeoutSeconds": 60,
  "providers": []
}
```

`databasePath` is resolved relative to the JSON file. `serverUrl` must be an HTTP(S) loopback URL without credentials. The CLI appends `/mcp`. `requestTimeoutSeconds` is the CLI operation timeout, 1..3600 seconds. Missing files, unknown properties, invalid JSON, and unsafe URLs fail explicitly. No environment fallback exists.

## Providers

`apiToken` is required and must contain exactly 64 hexadecimal characters. `config-init` generates 32 cryptographically random bytes for a new canonical configuration; existing files are never overwritten. Missing/invalid tokens prevent startup. All HTTP requests, including `get_version`, require `Authorization: Bearer <apiToken>`. CLI supplies it automatically; `configure codex|claude` writes the endpoint and header only into the current user's profile. Keep config, client files and rollback journals private. To rotate, stop the host, replace `apiToken` with a new random secret, rerun both client registrations, then restart host and clients. This inbound token is separate from the Provider/service tokens described below.

Each entry has `name`, `endpoint`, `toolPrefix`, and optional `repository` (default false). Endpoints are loopback HTTP(S). Names are unique. For the existing Githubie routing identifier use `githubbie`, prefix `github`, repository true; its assertions use canonical provider ID and audience `githubie`. Buckettie uses `buckettie`, prefix `bitbucket`, repository true. Built-in build providers are `csharp`, `node`, and `php`; a configured provider with the same name overrides it. KelpieSSH deployment uses name `server` (or canonical name `kelpiessh`) and Protocol v2 assertions. Internal `server` routing is signed with audience `kelpiessh`. Server deployment does not read or transmit a Moyai service token. Tokens remain in the service database, never JSON.

```json
[
  {"name":"githubbie","endpoint":"http://127.0.0.1:43121/mcp","toolPrefix":"github","repository":true},
  {"name":"server","endpoint":"http://127.0.0.1:45432/mcp","toolPrefix":"kelpie","repository":false}
]
```

Project `build_config_json` accepts `configuration` and an `artifacts` array with `name`, `artifact_type`, and project-relative `file_path`. A server deployment target must set `kelpieTarget` to the immutable KelpieSSH target ID and supply a destination path. Deployment requires one file artifact with a SHA-256 value. The fixed Protocol v2 scopes are described in the [Provider authentication operations contract](docs/specifications/provider-authentication-operations.md).

## Project Provider Repository mapping

Project `providerRepositoryId` is stored in `projects.provider_repository_id`, not in the service JSON. It identifies an existing Provider registration independently of the Moyai Project name. Set it using `project_provider_repository_set` or `moyaictl project-provider-repository-set`, with the current Project revision. Query it using `project_get` / Project lists. See [ADR 0011](docs/adr/0011-provider-repository-id.md).

When unset, Provider `repository` arguments use the Project name for backward compatibility. IDs retain their supplied case, contain 1–128 ASCII letters/digits/underscores/hyphens/dots, and start with a letter or digit. Explicit `clearMapping=true` clears the mapping; omitted/null IDs alone do not clear it. Basic Project update/configure/rename/archive operations preserve the mapping.

Schema v7 adds the nullable column without changing Project UUIDs, names, revisions, or linked WorkItems. The service's migration backup and transaction rules apply. Repository operations and Repository-backed release operations use the mapped registration ID while Assertion UUID, canonical Repository URL, audience, and scopes remain unchanged. Saving a mapping does not register a Repository or authorize an unrelated Repository; incorrect Provider bindings are rejected when delegated.

## Installation and migration

Settings Consumer supports the agreed Githubie and Buckettie routes. Require boolean `data.repository_settings_read/update` for Githubie or `data.operations.repository_settings_read/update` for Buckettie; Buckettie additionally requires Moyai integration mode for reads and updates. After runtime readiness and owner approval, add the exact entries below to the corresponding Provider's `assertionToolScopes`. Defaults remain absent/denied; existing configuration is retained during upgrades. Do not substitute commit/push scopes or legacy tokens. Provider owns persistence and human approval; source implementation does not enable installed scopes. See [ADR0012](docs/adr/0012-repository-settings-consumer.md).

```json
{
  "bitbucket_repository_settings_get": ["repository.read"],
  "bitbucket_repository_settings_update": ["repository.settings.write"]
}
```

For Githubie, use the same scopes with `github_repository_settings_get` and `github_repository_settings_update`. This fragment belongs inside `assertionToolScopes`, not at the configuration root. It is an operator-reviewed template, not an automatic grant.

Githubie's optional `local_commit_branches` field uses these same tools/scopes/capability flags; it is not a new configuration default or grant. It replaces only the explicitly supplied list and requires Provider approval. Buckettie's contract remains eight fields. A missing Settings tool mapping returns `provider_capability_missing` before HTTP; a missing/false Provider capability returns that code after negotiation. Inspect the selected runtime's config and version before changing scopes; endpoint reachability alone cannot enable a missing map. Installed config is loaded at startup, so an approved mapping change requires restarting only the intended runtime.

The MSI runs `profile-init` in the signed-in user's context. Explicit initialization creates the canonical configuration when absent, with a generated token and persisted loopback port. Database, logs and private signing keys remain user scoped. The configuration directory permits the configured Windows owner, SYSTEM and Administrators; MSI preserves the configured owner SID during repair/upgrade. The deployment supports one configured Windows owner.

For an existing user-host installation, back up both configuration files privately and explicitly promote the active settings to the canonical path, preserving the endpoint, token, absolute database path and signing identity. Retire the obsolete profile configuration only after successful verification. Do not activate a retained legacy Service configuration or import its DB/key automatically. Ordinary host startup never creates a missing configuration.

Do not install a development MSI over the running instance without approval. Canonical configuration, user data and logs are retained by uninstall; legacy Service data is preserved for rollback.

## Automatic Windows startup

New installations enable the tray controller at Windows logon. Host process state and autostart are independent. The control pipe is restricted to the current Windows user. Runtime database, key scope and logs remain user scoped; operational configuration and the persisted endpoint are owned by the configured Windows owner. Other users do not get an alternative configuration.

CLI management commands: `host start`, `host stop`, `host restart`, `host status`, and `autostart enable|disable|status`. Disabling autostart does not stop a running host, and stopping the host does not disable autostart. Business commands fail when that user's endpoint is unavailable and never fall back to another profile or SQLite.

Host output is stored under the user's LocalAppData logs directory. Uninstall removes only the product-owned autostart entry and binaries; user data and the legacy service data remain. Before release, validate user isolation, concurrent users, install/upgrade/rollback/uninstall, autostart persistence and data retention on Windows.
## Tray MCP server menu

The MCP server submenu is placed above the separator before Exit, preserving the other existing entries. It contains a non-clickable status row, a separator, Check status, Start, and Stop. Both the parent menu and status row show a 16×16 pill LED. Before the first check the status is unknown with a gray LED.

| State | Classification | LED base color |
| --- | --- | --- |
| Running | HTTP 2xx and JSON `status` equals `ok`; display `version` when present | Green RGB(40,190,70) |
| Not responding | No Health response, but a process matches the full installed `bin/host/Moyai.Mcp.exe` path | Yellow RGB(240,170,30) |
| Error | HTTP failure, malformed JSON, non-ok status, or operation failure/timeout | Red RGB(215,50,45) |
| Stopped | No matching process and no Health response | Gray RGB(150,150,150) |

The Health URL is the configured `serverUrl` origin plus `/health`. Missing/unreadable/malformed configuration or a non-loopback/non-HTTP(S) URL falls back to `http://127.0.0.1:43120/health`. Requests time out after 2 seconds and do not follow redirects. Only `GET /health` is anonymous and returns status/version without configuration or secrets. MCP authentication remains required; see [ADR 0027](docs/adr/0027-tray-mcp-server-health.md).

Opening the submenu refreshes status. Check status shows an informational balloon for running and a warning otherwise. Start is disabled when running; Stop is disabled when stopped. Start launches the installed user host directly and waits up to 20 seconds, polling at 0.5-second intervals. Stop asks for confirmation with No selected by default, explaining that all Codex and Claude Code sessions using Moyai disconnect until it starts again. Only a host with the matching executable path is gracefully stopped through the existing user control pipe. After up to 10 seconds, remaining matching processes are killed, with an additional 2-second verification deadline.

While starting/stopping, the status row shows the operation and both lifecycle commands are disabled. Success/failure is reported in informational/warning balloons. Live stop/start verification requires the user's consent because it interrupts other sessions.

