# Runtime diagnostics and tray host control

[English](runtime-diagnostics.md) | [日本語](runtime-diagnostics.ja.md)

Development2.0.4.1 allows one tray instance per Windows user and logon session. Further launches exit successfully before creating an icon or contacting the host, including launches with `--autostart`. Exit the tray normally to restart it; a crashed tray does not leave a stale lock. Existing host authentication and autostart settings are unchanged. When first updating from an older build, close its tray before starting the corrected tray; older processes do not participate in the new guard.

In2.0.3.2, tray status uses a modeless window available from the taskbar and Alt+Tab. Host restart remains available while the status window is open and refreshes its contents afterward. Close, Escape and the window close action hide only the status window; use the tray exit menu to exit the controller. Host commands are disabled only while the CLI operation runs.

Development candidate 2.0.2.0 adds MCP `runtime_diagnostics` and CLI `moyaictl runtime-diagnostics [--project <registered-name>]`. It reads the running host's loaded configuration, not a new copy used for routing. Existing MCP HTTP owner authentication still applies. Run `list_projects` first when selecting a Project.

The response projects `configuration` (config/DB paths, load time, loaded/current file timestamps, status and nullable restartRequired), Providers (name, prefix, endpoint origin, connection/authentication modes, repository flag and minimal authentication state), and optional Project routing diagnosis. Endpoint paths, queries and userinfo, tokens, scope maps, fingerprints, signing key/KEK lists and raw settings are excluded. Providers are not contacted; discovery, signing, key creation and configuration reload are not attempted.

For `windows_owner`, authentication state is `signature_not_required`. Assertion mode reads only existing DB metadata: `issuer_not_configured`, `active_key_missing`, `active_key_ambiguous`, `active_key_expired`, `active_key_not_yet_valid`, `protector_unavailable` or `metadata_unavailable`. `configured` means only public metadata and CNG key existence were checked; it does not promise that signing or Provider trust will succeed. Other protectors report `configured_not_verified`. Legacy mode reports `legacy_token_not_inspected` or `legacy_period_invalid` without reading token values. No key management API is added.

`unchanged` means file bytes match those loaded; `restart_required` means they differ. File timestamps alone do not decide this. Missing, unreadable or files larger than 4 MiB return missing/unavailable with restartRequired=null. These do not mean settings are applied. The host retains loaded settings and never automatically reloads or restarts.

`moyaictl host status` also reports configuration and host start time via the current-user-only control pipe. Older hosts can omit these fields; update the host to obtain the comparison. `provider_not_configured`/`provider_repository_disabled` Project diagnoses direct you to the loaded configuration and an explicit restart. Authentication state is an observation, not an authorization grant.

The tray menu offers **Restart MCP host** through the existing CLI restart sequence (validate settings, stop ACK, wait for that PID, start, ready). **Show host status** refreshes the configuration indicator and displays readable fields. The indicator is last observed state, refreshed on tray startup, status and lifecycle operations; it is not a file watcher. Lifecycle/status commands are serialized, have a 120-second CLI deadline and are never automatically retried. An unconfirmed result requires checking status before another operation. Exiting the tray leaves the MCP host running. Logon autostart remains independent.

Project metadata uses a separate read-only DB connection. If it cannot be read, `project_metadata_unavailable` replaces raw DB errors; a missing DB is not created. Local source/tests/publish do not constitute installed-host or GUI acceptance. Design and security conditions: [ADR0024](adr/0024-runtime-diagnostics-and-tray-host-restart.md).
