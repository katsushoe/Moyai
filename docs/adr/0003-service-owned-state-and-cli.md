# ADR 0003: Service-owned state and CLI

## Status
Accepted for business-command service ownership. Windows host lifetime, default profile storage, and management commands are superseded by [ADR 0019](0019-user-session-mcp-host.md). Historical v1.2 SCM validation remains evidence for the prior service implementation only.

## Context
The common service standard requires persistent configuration, service-connected business commands, and CLI lifecycle management. Separate CLI database access bypasses service state and configuration.

## Decision
Use the logged-on user's LocalAppData profile by default or an explicit `--config` path. Relative database paths resolve against the configuration directory. Do not read environment variables for host, database, endpoint, or Provider configuration. CLI business commands use the current user's MCP host tools and schemas, including `version`. An unavailable host returns an error; there is no local database or other-user profile fallback.

Expose `host start`, `host stop`, `host restart`, and `host status` for the current user, plus independent `autostart enable|disable|status` commands. A same-user local control channel manages the host process; there is no SCM lifecycle.

The MSI invokes `profile-init` under the signed-in user's identity and preserves existing per-user configuration on upgrade. It does not import the legacy service URL, database, tokens or key.

## Security and operations
Bind only to loopback. Configuration contains endpoint references, never service tokens. User tokens and key envelopes remain inside the active user's profile and identity. The local control pipe is current-user-only. CLI preserves host-side errors and never automatically retries mutations. Install/upgrade and release require the existing operational approvals.

## Alternatives
Keeping direct SQLite CLI access conflicts with service ownership. Environment variables are unsuitable for the requested persistent service configuration. A second business HTTP API would duplicate MCP contracts.

## Validation and documentation
Test JSON validation/path resolution, CLI schema conversion, business read/write/error flows through a running host, unavailable host, pause/resume admission, and MSI configuration/permissions. Update CONFIG, COMMANDS, MCP_SETUP and the service package test. Windows registration/start/pause/resume/stop/unregister and uninstall data retention must also be exercised in an isolated administrator environment before release.
