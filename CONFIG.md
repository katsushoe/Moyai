# Moyai configuration

[English](CONFIG.md) | [日本語](CONFIG.ja.md)

MCP client registration uses an explicitly selected user profile and preserves unrelated client settings. MSI properties: `MOYAI_CODEX`, `MOYAI_CLAUDE` (select with `1`), `MOYAI_CLIENT_PROFILE` (existing absolute profile path). See [MCP setup](MCP_SETUP.md#installer-client-registration) for ownership, uninstall and recovery behavior.

## Configuration file

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

Each entry has `name`, `endpoint`, `toolPrefix`, and optional `repository` (default false). Endpoints are loopback HTTP(S). Names are unique. For the existing Githubie routing identifier use `githubbie`, prefix `github`, repository true; Buckettie uses `buckettie`, prefix `bitbucket`, repository true. Built-in build providers are `csharp`, `node`, and `php`; a configured provider with the same name overrides it. KelpieSSH deployment uses name `server` and the configured tool prefix. Tokens remain in the service database, never JSON.

```json
{"name":"githubbie","endpoint":"http://127.0.0.1:43121/mcp","toolPrefix":"github","repository":true}
```

Project `build_config_json` accepts `configuration` and an `artifacts` array with `name`, `artifact_type`, and project-relative `file_path`. Deployment targets retain their existing Project configuration and secret handling.

## Installation and migration

The MSI runs `config-init` before starting the automatic `Moyai` service under LocalService. Initial configuration is generated only when absent; a previously saved registry `McpUrl` is imported once. Existing JSON is validated and retained. Edit JSON as administrator, then use `service stop` and `service start`. The legacy MSI URL property/registry does not override an existing JSON file.

Before upgrading an environment-configured installation, explicitly migrate its DB path, endpoint and Provider settings to JSON. Environment variables are no longer read. Do not install a development MSI over the running instance without approval. Configuration, data and logs are retained by uninstall; packaged binaries and the service registration are removed.

## Automatic Windows startup

The MSI registers Auto/LocalService. Configuration is readable by LocalService and writable by administrators; data/log directories permit LocalService modification. LocalService does not inherit a user's file permissions or mapped drives. Grant required project access explicitly or use external Providers.

CLI management commands: `service register`, `service start`, `service status`, `service pause`, `service resume`, `service stop`, `service unregister`. SCM changes require Windows permissions; status is read-only. Unregister requires Stopped and retains data/configuration. Pause rejects newly arriving HTTP requests with 503; requests already admitted may finish. Resume restores admission. Stop terminates the host. Business commands fail when the endpoint is unavailable and never fall back to SQLite.

Logs use console output and the Windows Application event log in service mode. Headless errors never display dialogs. For isolated testing only, launch `Moyai.Mcp.exe --config <isolated-json>` with a separate DB and port. Validate install/upgrade/pause/resume/uninstall/reboot and data retention on an isolated Windows administrator environment before release.
