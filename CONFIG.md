# Moyai configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

This document is the source of truth for environment configuration used by the CLI, MCP server, and Providers.

## Configuration Directory

Version 1.0.4 reads environment variables only. The MSI creates `C:\Moyai\config` but does not generate or load a configuration file.

## File Generation

Users set process environment variables. Moyai, the build, and the MSI do not generate settings or secrets.

## Main Settings

## Environment variables

- `MOYAI_DB_PATH`: SQLite database file path. Required by the CLI and MCP server.
- `MOYAI_MCP_URL`: Streamable HTTP listen URL. Required by the MCP server and restricted to a loopback host, for example `http://127.0.0.1:43120`.
- `GITHUBIE_MCP_URL`: Githubie Streamable HTTP MCP endpoint on a loopback host.
- `BUCKETTIE_MCP_URL`: Buckettie Streamable HTTP MCP endpoint on a loopback host.
- `MOYAI_BUILD_PROVIDER_NAME`, `MOYAI_BUILD_PROVIDER_URL`, `MOYAI_BUILD_PROVIDER_PREFIX`: optional build Provider identity, loopback MCP endpoint, and Tool prefix.
- `MOYAI_DEPLOY_PROVIDER_NAME`, `MOYAI_DEPLOY_PROVIDER_URL`, `MOYAI_DEPLOY_PROVIDER_PREFIX`: optional deploy Provider identity, loopback MCP endpoint, and Tool prefix.

All values are strings with no default. `MOYAI_DB_PATH` is required for CLI data operations and MCP; `MOYAI_MCP_URL` is required for MCP and must be an absolute loopback URL. Provider triplets are optional but register only when all three values are non-empty. URL values must identify the applicable Streamable HTTP endpoint.

## Profile Settings

`GITHUBIE_MCP_URL` and `BUCKETTIE_MCP_URL` register repository and lifecycle Providers. Build and deploy triplets register additional lifecycle Providers. Omitting a Provider setting leaves that capability unregistered.

## Samples

```powershell
$env:MOYAI_DB_PATH = 'C:\Moyai\data\moyai.db'
$env:MOYAI_MCP_URL = 'http://127.0.0.1:43120'
$env:GITHUBIE_MCP_URL = 'http://127.0.0.1:43121/mcp'
```

Do not include real token values in configuration examples, source, logs, or documentation.

The MCP endpoint is `${MOYAI_MCP_URL}/mcp`. Legacy SSE transport and browser CORS are not enabled.

## CLI

The CLI returns JSON to standard output and structured errors to standard error. Commands are `version`, `project-list`, `project-get`, `project-create`, `project-update`, `project-set-archived`, `work-item-list`, `work-item-get`, `work-item-create`, `work-item-update`, `work-item-set-deleted`, and `work-item-transition`.

Options use `--kebab-case`. Mutating commands require `--actor-type` and `--actor-name`; update, archive, delete, and transition commands also require `--expected-revision`.

Repository commands are `repository-status`, `repository-diff`, `repository-commit`, `repository-push`, and `repository-pull`. Authentication commands are `token-issue`, `token-rotate`, `token-revoke`, and `token-cleanup`. Lifecycle commands are `build`, `release-create`, `release-publish`, `release-withdraw`, and `deploy`.

## MCP

The MCP server uses stateless Streamable HTTP. It exposes CLI-equivalent Project and WorkItem operations plus the internal `auth_introspect` tool and the read-only `get_version` tool.

## Installation

The Windows MSI installer must install Moyai under `C:\Moyai`.

The x64 MSI is built with WiX Toolset. Run:

```powershell
.\scripts\Build-Installer.ps1
```

The installer places self-contained CLI and MCP binaries in `C:\Moyai\bin` and creates
`C:\Moyai\config`, `C:\Moyai\logs`, and `C:\Moyai\data`. The MSI does not ship settings,
secrets, user data, or logs. These directories and their user-created contents are preserved
when they are not empty during upgrade or uninstall.
