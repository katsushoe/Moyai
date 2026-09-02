# Moyai configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

This document is the source of truth for environment configuration used by the CLI, MCP server, and Providers.

## Configuration Directory

Moyai does not load a configuration file. The MSI creates `C:\Moyai\config`. The MCP host reads environment variables and accepts `--MOYAI_DB_PATH` and `--MOYAI_MCP_URL` arguments, which take precedence over those variables. The CLI still reads environment variables only.

## File Generation

Users set process environment variables for manual execution. The MSI persists the service listen URL in the 64-bit registry at `HKLM\Software\Akatsukisoft\Moyai`, value `McpUrl`, and supplies service arguments. It never generates secrets.

## Main Settings

## Environment variables

- `MOYAI_DB_PATH`: SQLite database file path. Required by the CLI and MCP server.
- `MOYAI_MCP_URL`: Streamable HTTP listen URL. Required by the MCP server and restricted to a loopback host, for example `http://127.0.0.1:43120`.
- `GITHUBIE_MCP_URL`: Githubie Streamable HTTP MCP endpoint on a loopback host.
- `BUCKETTIE_MCP_URL`: Buckettie Streamable HTTP MCP endpoint on a loopback host.
- `MOYAI_BUILD_PROVIDER_NAME`, `MOYAI_BUILD_PROVIDER_URL`, `MOYAI_BUILD_PROVIDER_PREFIX`: optional build Provider identity, loopback MCP endpoint, and Tool prefix.
- `MOYAI_DEPLOY_PROVIDER_NAME`, `MOYAI_DEPLOY_PROVIDER_URL`, `MOYAI_DEPLOY_PROVIDER_PREFIX`: optional deploy Provider identity, loopback MCP endpoint, and Tool prefix.

For Server Deploy through KelpieSSH, set the deploy Provider name to `server`; Moyai stores only the Kelpie target name/ID in the Deployment Target and never stores SSH credentials. Local targets use the Project `install_path` by default.

The built-in Build Providers are `csharp`, `node`, and `php`. `build_config_json` may contain `configuration` and an `artifacts` array. Each artifact requires `name`, `artifact_type`, and a project-relative `file_path`. Moyai hashes files with SHA-256 and directories with a deterministic relative-path/file-hash manifest. An external Provider with the same configured name overrides the built-in Provider.

All environment values are strings with no application default. `MOYAI_DB_PATH` is required for CLI data operations and MCP; `MOYAI_MCP_URL` is required for MCP and must be an absolute HTTP(S) loopback URL. Provider triplets are optional but register only when all three values are non-empty. URL values must identify the applicable Streamable HTTP endpoint.

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

### Automatic Windows startup

The MSI registers the `Moyai` Windows service (display name `Moyai MCP`) with automatic startup and starts it during installation. No user logon or manual `sc.exe create` is required. The service runs as `NT AUTHORITY\LocalService`, not as the installing user or LocalSystem. It uses `C:\Moyai\data\moyai.db`; an existing database at that path is reused, not replaced. Data and logs directories receive inherited LocalService modify permissions. Files with explicitly protected ACLs must be checked separately by the administrator.

On first installation, the listen URL defaults to `http://127.0.0.1:43120`. To select another loopback URL, pass the MSI property `MOYAI_MCP_URL`. The saved registry value takes precedence on repair, upgrade, and reinstall. To change it later, update `McpUrl` as administrator and repair the MSI so the service arguments are regenerated. The saved URL is retained on uninstall. Never place secrets in these settings or command-line arguments.

Before migrating from manual execution, stop the existing MCP process using the same port. A port collision or invalid configuration causes service startup to fail; the MSI must not be treated as successfully installed if starting the service fails. The MSI stops the service before replacing/removing its executable and removes the service on uninstall. It does not remove the database or user-created configuration/log files. Use a newer product version for major-upgrade testing; the development MSI is not a published release.

Provider environment variables must be available to the service account, not merely to an interactive user process. Machine environment changes may require a Windows restart to reach the Service Control Manager. LocalService does not inherit the user's filesystem access, PATH, credentials, or mapped drives. Grant only the required project access or use configured external Providers; do not elevate the service to LocalSystem as a workaround.

Service warnings and errors are written to Windows Application event log using the existing `Application` source. Fatal startup errors include the `Moyai MCP` prefix. No error dialog is shown in service mode. Manual execution retains console logging and requires its own settings.

```powershell
Get-Service -Name Moyai
Stop-Service -Name Moyai
Start-Service -Name Moyai
```

Service control requires appropriate Windows permissions. Verify installation, upgrade, uninstall, reboot startup, and preservation of the existing database on an isolated Windows test machine before release.
