# MCP Setup

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## Values and Placeholders

| Value | How to obtain | Example | Change when |
| :--- | :--- | :--- | :--- |
| Database path | Choose a writable local path | `C:\Moyai\data\moyai.db` | Moving data |
| Server URL | Choose an unused loopback port | `http://127.0.0.1:43120` | Port conflict |

Examples contain complete values; do not enter angle-bracket placeholders.

## Prerequisites

Install the x64 MSI in `C:\Moyai`. MSI installation and service management require appropriate Windows permissions.

## Authentication and Configuration

Configuration: `config/moyai.json`. See [CONFIG](CONFIG.md). CLI business commands connect to the running service.

## Start the Server

```powershell
& 'C:\Moyai\bin\moyaictl.exe' service start
```

The pass condition is a log entry stating that the server is listening on the configured URL.

## Register Clients

## Installer client registration

The MSI dialog lets you select Codex and/or Claude Code and enter an existing absolute user profile directory. Close the selected clients first. Silent installation accepts `MOYAI_CODEX=1`, `MOYAI_CLAUDE=1`, and `MOYAI_CLIENT_PROFILE="%USERPROFILE%"`; without selection it installs only the service. The selected profile and clients are retained for repair, upgrade and uninstall. Uninstall removes only entries created by Moyai that have not been edited. Major upgrades keep registration.

The installer calls the same local management commands available after installation:

```powershell
& 'C:\Moyai\bin\moyaictl.exe' configure codex --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' configure claude --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' unconfigure codex --profile $env:USERPROFILE
& 'C:\Moyai\bin\moyaictl.exe' unconfigure claude --profile $env:USERPROFILE
```

Omit `--profile` to use the CLI user's profile. `configure` reads the endpoint from `--config` (default: the installed service configuration); `unconfigure` works without the service or its configuration. Neither command launches a client or accesses the database. A client can be preconfigured before installation.

Codex uses `<profile>/.codex/config.toml`; Claude Code uses `<profile>/.claude.json` (user scope). Custom client configuration roots require manual registration. Existing identical entries stay user-owned; differing entries and edited owned entries produce an error without overwriting them. Unrelated settings are retained; formatting may normalize. Ownership and temporary rollback journals are under `<profile>/.moyai`. Journals may contain existing configuration secrets: keep them private. MSI rollback restores the original files; commit removes the journal.

For an interrupted transaction, close the client and inspect the failure before running `moyaictl client-transaction codex --phase rollback --profile <profile>` (or `claude`). Use `--phase commit` only to retain the applied state and discard its recovery journal. Additional users must run configure/unconfigure in their own user context. Restart clients to discover the tools. See [ADR 0005](docs/adr/0005-client-registration.md).



### Codex

Add the following Streamable HTTP server to the user MCP configuration, then reload Codex:

```toml
[mcp_servers.moyai]
url = "http://127.0.0.1:43120/mcp"
```

Server name is `moyai`, transport is Streamable HTTP, authentication is not required for `get_version`, and the configuration is user-scoped. The exact user configuration path is determined by the installed Codex version.

### Claude Code

Merge this complete server entry into the user MCP configuration, then restart Claude Code:

```json
{
  "mcpServers": {
    "moyai": {
      "type": "http",
      "url": "http://127.0.0.1:43120/mcp"
    }
  }
}
```

Server name is `moyai`, transport is Streamable HTTP, and authentication is not required for `get_version`. Use the user configuration location documented by the installed Claude Code version.

## Multiple Workspaces

Use one server process and database per isolation boundary. Assign a distinct loopback port and database path to each process.

## Verify the Connection

1. Confirm the endpoint is listening.
2. Confirm the client discovers tools.
3. Confirm `get_version` returns `Moyai` and the installed version.
4. Confirm `list_projects` returns JSON-compatible structured data. The server instructs AI clients to call it before every project operation; `project_list` remains available for compatibility.
5. Stop at the first failed stage and inspect server standard error.

## Troubleshooting

Configuration: `config/moyai.json`. See [CONFIG](CONFIG.md). CLI business commands connect to the running service.
Configuration: `config/moyai.json`. See [CONFIG](CONFIG.md). CLI business commands connect to the running service.
- Port conflict: select another unused loopback port in both server and client settings.
- No tools: restart or reload the client after updating its configuration.
