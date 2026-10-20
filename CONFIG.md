# Moyai configuration

## Buckettie Diff remote selection (development)

An existing Project `gitRemoteName` is forwarded unchanged to Buckettie Diff only when nonblank and the Provider advertises `remote_resolution` version1/mode `repository_url`. Unset/blank values skip negotiation; absent/invalid/failed/unsupported capability omits remote for compatibility, not proof that an explicit name was validated. Githubie Diff still omits remote pending Provider validation confirmation. No new setting, default, DB migration or scope; reuse `repository.read`. No installed config/remote mutation. See [ADR0016](docs/adr/0016-buckettie-diff-remote.md).

## Pull request creation authorization (development)

After capability/contract readiness and operator approval, add exact `assertionToolScopes` entries `"github_pr_create": ["provider.githubie.pull_request.write"]` or `"bitbucket_pr_create": ["provider.buckettie.pr_create"]` to the corresponding Provider. These are string-array dictionary values; default is absent and denied. Existing read/commit/push authorization does not enable them. Require `data.pr_create=true` for Githubie, `data.operations.pr_create=true` and integration_mode=moyai for Buckettie. Explicit operation flags are pending owner agreement/rollout; tool availability alone is insufficient. No top-level setting, installed scope or Provider allowed-route policy is changed automatically. See [ADR0015](docs/adr/0015-provider-pull-request-create.md).

## Local-branch creation authorization (development)

After Githubie advertises `data.local_branch_create=true`, an administrator may add `github_local_branch_create` to its `assertionToolScopes` dictionary with exact string-array value `["repository.branch.write"]`. Entry example: `"github_local_branch_create": ["repository.branch.write"]`. Default: absent and denied. Generic write/remote branch authorization does not enable this tool. No new top-level keys; implementation never edits installed scopes, keys, accounts or DB. Buckettie has no confirmed local-create mapping and remains unsupported. See [ADR0014](docs/adr/0014-provider-local-branch-create.md).

## Branch-switch authorization (development1.3.12.0)

After the Provider runtime explicitly advertises branch_switch and the administrator authorizes enablement, configure github_branch_switch: [repository.branch.write] or buckettie_branch_switch: [repository.branch.write] in the respective assertionToolScopes. Buckettie's configured prefix remains bitbucket; only this operation resolves to buckettie_branch_switch. Require Moyai integration mode for Buckettie. Implementation does not edit installed scopes, signing keys, DB or service account. See [ADR0013](docs/adr/0013-provider-branch-switch.md).

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

MCP client registration uses an explicitly selected user profile and preserves unrelated client settings. MSI properties: `MOYAI_CODEX`, `MOYAI_CLAUDE` (select with `1`), `MOYAI_CLIENT_PROFILE` (existing absolute profile path). See [MCP setup](MCP_SETUP.md#installer-client-registration) for ownership, uninstall and recovery behavior.

## Configuration file

Repository authentication defaults to ES256 assertions and fails closed until configured. See [Provider authentication settings, key initialization and migration](docs/specifications/provider-authentication-operations.md). Static repository tokens require an explicitly bounded migration window of at most seven days.

The service and CLI read `config/moyai.json`, relative to the installation root (the parent of `bin`). `--config <path>` selects another file. Configuration never comes from environment variables. The CLI uses the endpoint only and never opens the database. Changes take effect after restarting the service.

```json
{
  "databasePath": "../data/moyai.db",
  "serverUrl": "http://127.0.0.1:43120",
  "requestTimeoutSeconds": 60,
  "providers": []
}
```

`databasePath` is resolved relative to the JSON file. `serverUrl` must be an HTTP(S) loopback URL without credentials. The CLI appends `/mcp`. `requestTimeoutSeconds` is the CLI operation timeout, 1..3600 seconds. Missing files, unknown properties, invalid JSON, and unsafe URLs fail explicitly. No environment fallback exists.

## Providers

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

The MSI runs `config-init` before starting the automatic `Moyai` service under LocalService. Initial configuration is generated only when absent; a previously saved registry `McpUrl` is imported once. Existing JSON is validated and retained. Edit JSON as administrator, then use `service stop` and `service start`. The legacy MSI URL property/registry does not override an existing JSON file.

Before upgrading an environment-configured installation, explicitly migrate its DB path, endpoint and Provider settings to JSON. Environment variables are no longer read. Do not install a development MSI over the running instance without approval. Configuration, data and logs are retained by uninstall; packaged binaries and the service registration are removed.

## Automatic Windows startup

The MSI registers Auto/LocalService. Configuration is readable by LocalService and writable by administrators; data/log directories permit LocalService modification. LocalService does not inherit a user's file permissions or mapped drives. Grant required project access explicitly or use external Providers.

CLI management commands: `service register`, `service start`, `service status`, `service pause`, `service resume`, `service stop`, `service unregister`. SCM changes require Windows permissions; status is read-only. Unregister requires Stopped and retains data/configuration. Pause rejects newly arriving HTTP requests with 503; requests already admitted may finish. Resume restores admission. Stop terminates the host. Business commands fail when the endpoint is unavailable and never fall back to SQLite.

Logs use console output and the Windows Application event log in service mode. Headless errors never display dialogs. For isolated testing only, launch `Moyai.Mcp.exe --config <isolated-json>` with a separate DB and port. Validate install/upgrade/pause/resume/uninstall/reboot and data retention on an isolated Windows administrator environment before release.
