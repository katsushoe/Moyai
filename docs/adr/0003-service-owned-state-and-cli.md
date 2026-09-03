# ADR 0003: Service-owned state and CLI

## Status
Accepted for implementation. Supersedes the environment and direct-DB CLI design in ADR 0002. Live upgrade and isolated Windows SCM lifecycle validation are release gates. For 1.2.0, live upgrade passed 14 checks and VirtualBox guest validation passed 17 checks on 2026-09-03, including register/unregister, uninstall DB/configuration hash preservation, and reinstall data recovery. Evidence: artifacts/project-upgrade-120/verification.json and artifacts/project-upgrade-120/vm-test/guest-result.json.

## Context
The common service standard requires persistent configuration, service-connected business commands, and CLI lifecycle management. Separate CLI database access bypasses service state and configuration.

## Decision
Use `config/moyai.json`, resolved relative to the installed executables, or an explicit `--config` path. Relative database paths resolve against the configuration directory. Do not read environment variables for host, database, endpoint, or Provider configuration. CLI business commands use the service's MCP tools and schemas, including `version`. A stopped/unreachable/paused service returns an error; there is no local database fallback.

Expose `service start`, `service stop`, `service pause`, `service resume`, `service register`, `service unregister`, and `service status` using Windows SCM. Pause rejects new HTTP requests with 503 while allowing requests already admitted to complete; resume admits new requests. Stop terminates the service. Registration uses the installed executable and explicit configuration file, Auto and LocalService. Unregister requires a stopped service and retains configuration and data.

The MSI invokes CLI `config-init` after installing binaries and before starting the service. This writes defaults only when the configuration is absent, imports the previously persisted MCP URL, and does not package or replace user configuration. Existing environment-only Provider configuration requires explicit migration to JSON before upgrading.

## Security and operations
Bind only to loopback. Configuration contains endpoint references, never service tokens. Provider tokens remain service-owned. Configuration grants LocalService read access; data/log directories grant modify access. SCM mutations require Windows permissions. CLI preserves service-side errors and never automatically retries mutations. Install/upgrade and release require the existing operational approvals.

## Alternatives
Keeping direct SQLite CLI access conflicts with service ownership. Environment variables are unsuitable for the requested persistent service configuration. A second business HTTP API would duplicate MCP contracts.

## Validation and documentation
Test JSON validation/path resolution, CLI schema conversion, business read/write/error flows through a running host, unavailable host, pause/resume admission, and MSI configuration/permissions. Update CONFIG, COMMANDS, MCP_SETUP and the service package test. Windows registration/start/pause/resume/stop/unregister and uninstall data retention must also be exercised in an isolated administrator environment before release.
