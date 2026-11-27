# MCP Setup

## PR source publication tools (development2.0.4.0)

Dedicated `pull_request_publish_create` and `pull_request_publish_status` share the service-connected CLI schema. Create requires an explicit operation UUID and draft boolean, both publication capabilities, and owner authentication or both push/PR-write assertion scopes. Unknown and partial outcomes require status reconciliation without automatic mutation retry. See [operation guide](docs/pr-source-publication.md). Existing PR creation never publishes a branch.

## Hosting branch protection tools

Development2.0.3.0 exposes branch_protection_get/update/delete with the same contract as CLI. Get is read-only; update/delete require Provider Desktop approval and durable Moyai audit. Explicit capabilities and authenticated authority are required; unknown outcomes are not replayed. See [hosting protection](docs/branch-protection.md).

## Host diagnostics

MCP `runtime_diagnostics` is read-only and requires existing owner authentication. See [diagnostics](docs/runtime-diagnostics.md) for safe output and explicit tray restart.


## PR state tools (development2.0.1.0)

The existing Moyai connection exposes pull_request_get (read-only) and pull_request_merge (destructive/open-world) with the same registered Project selection; no new endpoint or client credential is introduced. Required merge inputs are project, pullRequestId, strategy and expectedSourceCommit; message is optional. The service-connected CLI uses the actual MCP schema for the corresponding hyphenated commands. Read [commands](COMMANDS.md) and [authorization](CONFIG.md) before any real merge. Explicit approval must name the PR, strategy and full source SHA. Unknown/pending results require a separate get, never an automatic merge retry. This local candidate enables Githubie dispatch only; it does not install a Provider, change grants or prove live acceptance.

For command-line requests to the running MCP service, use `moyaimcp.exe <command>`. It shares the `moyaictl.exe` implementation and packaged dependencies, including Provider approval and structured errors. `Moyai.Mcp.exe` is the service host. The Windows MSI includes both CLI names. `--help`/`--version` are local; `commands`/`version` query the configured service. See [commands](COMMANDS.md#mcp-command-client).

The development Githubie Settings extension accepts `changes.local_commit_branches` through the existing `repository_settings_update` object; no new MCP/CLI tool or scope is added. Values are exact replacement arrays, including explicit empty arrays. GET omits this optional field if the Provider omits it. Buckettie retains the common eight-field contract. Provider human approval and the installed Settings tool-scope mappings remain mandatory; source publication does not enable them.

Buckettie Diff remote forwarding uses the existing `repository_diff` tool and `repository.read` assertion. There is no additional client input or scope grant: select a registered Project and verify its remote-resolution capability/configuration as described in [CONFIG](CONFIG.md). Explicit remote rejection never falls back to origin. Githubie Diff forwarding remains excluded; publish does not establish installed or live acceptance. See [ADR0016](docs/adr/0016-buckettie-diff-remote.md).

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## Values and Placeholders

Repository Settings uses the same `repository_settings_get/update` tools for both Githubie and Buckettie. The registered Project selects the Provider and Repository binding. Before enabling it, verify exact capability flags, assertion trust and dedicated read/write scopes in [CONFIG](CONFIG.md). Buckettie reads and updates require Moyai integration mode. Updates require Provider human approval; unknown outcomes need a separate read, never automatic replay. Publishing does not change installed configuration or scopes.

| Value | How to obtain | Example | Change when |
| :--- | :--- | :--- | :--- |
| Database path | Choose a writable local path | `C:\Moyai\data\moyai.db` | Moving data |
| Server URL | Choose an unused loopback port | `http://127.0.0.1:43120` | Port conflict |

Client templates contain placeholders: use the persisted port and token from the canonical configuration, or let CLI registration write them.

## Prerequisites

The2.0.3.1 MSI requires .NET Framework4.8 for the embedded owner resolver; application runtimes are bundled. An unresolvable configured owner fails before removing an existing installation.

Choose the installation directory in the x64 MSI; silent installs accept `INSTALLFOLDER`. Repair/upgrade retains the selected directory. Replace `C:\Moyai` in the examples below with your actual installation directory. MSI installation requires appropriate Windows permissions. The MCP host and tray run as the signed-in user, without a Windows Service.

## Authentication and Configuration

Version 2.0 requires the per-user `apiToken` on every HTTP request. Explicit initialization generates it in the canonical configuration when absent. Use `configure codex` and `configure claude` to register the persisted endpoint and Bearer header without copying secrets into command arguments. Only the current user's profile is accepted. Missing or foreign credentials return HTTP 401. Protect all generated client files and journals as secrets.

The sole operational configuration is `<INSTALLFOLDER>\config\moyai.json`; other `--config` paths are rejected. See [CONFIG](CONFIG.md). CLI business commands connect to the current user's running host.

## Start the Server

```powershell
& 'C:\Moyai\bin\moyaictl.exe' host start
```

The tray starts the host at logon by default for new profiles. Its menu separates host start/stop/status from the autostart toggle. CLI alternatives are `host start|stop|restart|status` and `autostart enable|disable|status`.

## Register Clients

## Installer client registration

The MSI dialog lets you select Codex and/or Claude Code and enter the current Windows user's absolute profile directory. Other users' profiles are rejected. Close the selected clients first. Silent installation accepts `MOYAI_CODEX=1`, `MOYAI_CLAUDE=1`, and `MOYAI_CLIENT_PROFILE="%USERPROFILE%"`; client selection is optional. The selected profile and clients are retained for repair and upgrade. Uninstall removes only entries created by Moyai that have not been edited. Major upgrades keep registration.

The installer calls the same local management commands available after installation:

```powershell
& 'C:\Moyai\bin\moyaictl.exe' configure codex --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' configure claude --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' unconfigure codex --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' unconfigure claude --profile $env:USERPROFILE
```

Omit `--profile` to use the CLI user's profile; any explicit path must resolve to that same profile. `configure` reads the endpoint and API token only from `<INSTALLFOLDER>\config\moyai.json`; `unconfigure` works without the host or its configuration. Neither command launches a client or accesses the database. A client can be preconfigured before installation.

Codex uses `<profile>/.codex/config.toml`; Claude Code uses `<profile>/.claude.json` (user scope). Custom client configuration roots require manual registration. Existing identical entries stay user-owned; differing entries and edited owned entries produce an error without overwriting them. Unrelated settings are retained; formatting may normalize. Ownership and temporary rollback journals are under `<profile>/.moyai`. Journals may contain existing configuration secrets: keep them private. MSI rollback restores the original files; commit removes the journal.

For an interrupted transaction, close the client and inspect the failure before running `moyaictl client-transaction codex --phase rollback --profile <profile>` (or `claude`). Use `--phase commit` only to retain the applied state and discard its recovery journal. Additional users must run configure/unconfigure in their own user context. Restart clients to discover the tools. See [ADR 0005](docs/adr/0005-client-registration.md).



### Codex

Add the following Streamable HTTP server to the user MCP configuration, then reload Codex:

```toml
[mcp_servers.moyai]
url = "http://127.0.0.1:43120/mcp"
http_headers = { Authorization = "Bearer <profile-apiToken>" }
```

Server name is `moyai`, transport is Streamable HTTP, and the configuration is user-scoped. Replace the example port and token with the canonical configuration values, or use CLI registration. The header format follows the [Codex configuration reference](https://developers.openai.com/codex/config-reference). All tools, including `get_version`, require authentication.

### Claude Code

Merge this complete server entry into the user MCP configuration, then restart Claude Code:

```json
{
  "mcpServers": {
    "moyai": {
      "type": "http",
      "url": "http://127.0.0.1:43120/mcp",
      "headers": { "Authorization": "Bearer <profile-apiToken>" }
    }
  }
}
```

Server name is `moyai`, transport is Streamable HTTP, and every request requires authentication. Replace the example port and token with profile values, or use CLI registration. The `headers` field follows the [Claude Code MCP documentation](https://code.claude.com/docs/en/mcp).

## Multiple Workspaces

Use one server process and database per isolation boundary. Assign a distinct loopback port and database path to each process.

## Verify the Connection

1. Confirm the endpoint is listening.
2. Confirm the client discovers tools.
3. Confirm `get_version` returns `Moyai` and the installed version.
4. Confirm `list_projects` returns JSON-compatible structured data. The server instructs AI clients to call it before every project operation; `project_list` remains available for compatibility.
5. Stop at the first failed stage and inspect server standard error.

## Troubleshooting

Sole configuration: `<INSTALLFOLDER>\config\moyai.json`. See [CONFIG](CONFIG.md). CLI business commands connect to the running service.
Sole configuration: `<INSTALLFOLDER>\config\moyai.json`. See [CONFIG](CONFIG.md). CLI business commands connect to the running service.
- Port conflict: select another unused loopback port in both server and client settings.
- No tools: restart or reload the client after updating its configuration.
