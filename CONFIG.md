# Moyai configuration

## Environment variables

- `MOYAI_DB_PATH`: SQLite database file path. Required by the CLI and MCP server.
- `MOYAI_MCP_URL`: Streamable HTTP listen URL. Required by the MCP server and restricted to a loopback host, for example `http://127.0.0.1:43120`.

The MCP endpoint is `${MOYAI_MCP_URL}/mcp`. Legacy SSE transport and browser CORS are not enabled.

## CLI

The CLI returns JSON to standard output and structured errors to standard error. Commands are `version`, `project-list`, `project-get`, `project-create`, `project-update`, `project-set-archived`, `work-item-list`, `work-item-get`, `work-item-create`, `work-item-update`, `work-item-set-deleted`, and `work-item-transition`.

Options use `--kebab-case`. Mutating commands require `--actor-type` and `--actor-name`; update, archive, delete, and transition commands also require `--expected-revision`.

## MCP

The MCP server uses stateless Streamable HTTP. It exposes CLI-equivalent Project and WorkItem operations plus the internal `auth_introspect` tool and the read-only `get_version` tool.

## Installation

The Windows MSI installer must install Moyai under `C:\Moyai`.
