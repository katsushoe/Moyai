# ADR 0027: Tray MCP server health and lifecycle

Status: Accepted (2026-11-26 user request).

## Context

The tray must use the common five-display-state contract (unknown plus four observed states), pill LEDs, Health-based readiness, and confirmed stopping. Moyai already runs a per-user host with a current-user control pipe, not a Windows service or scheduled task.

## Decision

Add a minimal anonymous GET `/health` before token authentication, returning only `status: ok` and the host assembly version. All other requests retain existing authentication. The listener remains loopback-only under existing configuration validation. The tray resolves the configured loopback origin on each probe, falling back to the settings model's default origin when unreadable or unsafe, with a 2-second timeout and no redirects/proxy.

Use the registered installation's `bin/host/Moyai.Mcp.exe` full path for process discovery, launch, and termination. Launch directly with the canonical configuration, retaining configuration validation. Stop through the current-user pipe only after validating the responding PID's executable path. Wait at most 10 seconds for graceful termination, then kill only matching processes and bound verification to 2 seconds. Startup waits at most 20 seconds for Health; an existing matching process is not duplicated. UI commands are serialized and asynchronous; stopping requires a default-No warning that all product Codex/Claude Code sessions disconnect until restart.

## Alternatives

- CLI start/stop: retained for existing management, but its startup budget is 60 seconds and does not supply the common tray's bounded Health readiness.
- Scheduled task or service: rejected because neither is Moyai's existing user-host execution model.
- Bearer-authenticated Health: rejected for this minimal public readiness/version endpoint; no operational data or secrets are exposed.

## Scope, security and operation

Changes cover the host endpoint, tray controller/menus/resources/LEDs, tests, and configuration documentation. No database schema or remote Provider behavior changes. Other installations are excluded by full executable path. Process inspection failures are logged; termination never falls back to process-name-only targeting. A shared HTTP client bounds response size. The Health endpoint is process readiness, not database/Provider diagnostics.

## Validation and documentation

Test URL fallback/loopback validation, HTTP/JSON classification, authentication boundaries, UI lifecycle guards, localized stop warnings, and LED export. CONFIG.md and CONFIG.ja.md describe menu/state/color/timeouts. Export all four LEDs and inspect them visually. Build with no warnings/errors and run the entire test suite. Publishing local artifacts is allowed; installed live stop/start requires explicit user consent and remains separate from automated tests.
