# MCP Setup

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## Values and Placeholders

| Value | How to obtain | Example | Change when |
| :--- | :--- | :--- | :--- |
| Database path | Choose a writable local path | `C:\Moyai\data\moyai.db` | Moving data |
| Server URL | Choose an unused loopback port | `http://127.0.0.1:43120` | Port conflict |

Examples contain complete values; do not enter angle-bracket placeholders.

## Prerequisites

Install the x64 MSI in `C:\Moyai`. Administrator rights are required only for MSI installation.

## Authentication and Environment

Set `MOYAI_DB_PATH` and `MOYAI_MCP_URL`. Moyai binds only to loopback. Provider service tokens must be supplied through the client or provider secret mechanism and must not be stored in this file.

## Start the Server

```powershell
$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'
$env:MOYAI_MCP_URL = 'http://127.0.0.1:43120'
& 'C:\Moyai\bin\Moyai.Mcp.exe'
```

The pass condition is a log entry stating that the server is listening on the configured URL.

## Register Clients

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
3. Confirm `get_version` returns `Moyai` and `1.0.1.0`.
4. Confirm `project_list` returns JSON-compatible structured data.
5. Stop at the first failed stage and inspect server standard error.

## Troubleshooting

- Missing `MOYAI_DB_PATH`: set a writable database path.
- Missing or non-loopback `MOYAI_MCP_URL`: use `127.0.0.1` or `localhost`.
- Port conflict: select another unused loopback port in both server and client settings.
- No tools: restart or reload the client after updating its configuration.
