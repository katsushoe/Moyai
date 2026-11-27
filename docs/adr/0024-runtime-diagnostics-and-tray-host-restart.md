# ADR0024: Loaded-configuration diagnostics and explicit tray host restart

## Status and context

Accepted for the local candidate on2026-10-30. CR-2026-10-25-provider-auth-diagnostics and CR-2026-10-25-tray-host-restart require distinguishing the running host's loaded configuration from the current file, and the tray lifetime from the MCP host lifetime. The diagnostics CR's October26 amendment excludes new signing-key/KEK listing APIs; owner authentication is primary. Actual Windows GUI/installed acceptance remains separate.

## Decision

Load settings and capture the exact loaded bytes in one read. Keep only an in-memory private fingerprint for comparison, never expose/persist it or the API token. Public state reports the canonical loaded config/DB paths, load time and observed file write times, with unchanged/restart_required/missing/unavailable. Compare bounded current bytes; an unreadable or oversized file is unknown, not unchanged. A content change means restart is required even if its timestamp is preserved. No auto-reload, implicit host stop, configuration rewrite or alternate profile fallback is added.

Expose read-only runtime_diagnostics via MCP and the existing service-connected CLI. Project is optional; a selected registered Project adds exact configured-Provider/repository-role context. Project no longer needs source inspection to find a missing Provider. Output explicitly projects nonsecret fields, strips endpoint path/query/fragment and redacts the loaded API token from public strings. Do not serialize settings, credentials, scope maps, key material, discovery records or fingerprints. Do not contact Providers or a broker, issue signatures, initialize DBs/keys or mutate registration. Owner providers report signatures not required; assertion providers get minimal read-only key-validity/protector availability metadata, not key/KEK lists. CNG availability may use a read-only existence check; other unprobed protectors remain explicitly unverified.

Extend current-user control status with optional loaded-configuration state and host start time. Older hosts lacking those fields remain distinguishable as diagnostics unavailable. Tray adds explicit restart using the existing CLI host restart sequence (validate before stop, wait old PID exit, then start/readiness); it never treats exiting the tray as stopping the host. Status shows human-readable lifecycle/configuration state and a refreshable configuration warning. No secret JSON dump or automatic restart. Serialize tray lifecycle actions to prevent overlapping start/stop/restart requests.

## Alternatives and limits

The2026-11-02 installed2.0.3.1 acceptance reported disabled restart controls and a tray menu that could not be dismissed after status display. Replace the hidden owner's modal status/error message boxes with the existing MainForm as a modeless, taskbar-visible status window. Close the popup before dispatch, restore lifecycle controls in the operation's finally block, then display the result. A visible status window refreshes after subsequent explicit operations. Close/Escape/window-close hide only status; only the explicit exit item closes the tray. Keep serialized, bounded CLI execution and safe error text unchanged. Verify actual WinForms STA interaction with a fake CLI as well as installed GUI behavior; unit success alone is not installed acceptance.

- Hidden-owner modal status display: replaced because it holds lifecycle controls disabled until the dialog is dismissed and the reported installed UI could not dismiss it. A modeless taskbar window keeps both the result and lifecycle actions reachable.

- Timestamp-only staleness: not chosen, because preserved timestamps can hide a changed setting and touched timestamps need not mean content changed.
- Reparse current settings as the running configuration: rejected; it would conceal the settings actually used by the running host.
- New key-management/listing tools or signing/decryption probes: excluded by the narrowed CR. Missing/expired metadata and unverified protector access are distinct from confirmed operational readiness.
- Restart host when the tray exits or reload configuration automatically: rejected; host and tray lifetimes remain separate and configuration is applied only through explicit host restart.

## Verification and operation

### Tray process ownership (2026-11-03 amendment)

The2.0.4.0 installed acceptance reproduced two tray processes after a second launch. In development2.0.4.1, acquire a named Windows mutex before application initialization or MainForm construction. Use the Local session namespace and current Windows user SID, with a product identifier independent of version and installation path. Hold ownership on the main STA thread for the complete message loop. An existing owner makes a duplicate return successfully without a window, host status/start call, autostart update or existing-window activation. This includes duplicates launched with --autostart. Normal exit and exceptions release ownership; an abandoned owner is acquired so crash recovery does not require deleting a file or killing another process.

This is process coordination, not an authentication or authorization boundary. Existing host pipe/HTTP authentication and config ACL remain authoritative. Other Windows users and sessions have independent tray ownership. Concurrent same-user sessions still manage the existing per-user host according to its existing contract. An inaccessible mutex follows the existing safe top-level error handling; do not fall back to another name or start another tray. A pre-amendment tray does not own this mutex, so first installation of the correction must close the old tray before launching the corrected binary. It does not retroactively remove legacy processes.

Alternatives: process-name enumeration races and conflates users; a lock file requires stale-file recovery; a global cross-session mutex prevents legitimate session-local icons. A second-instance activation protocol is unnecessary for a tray without a requested window. Keep this guard local to the Windows tray and add no business MCP/CLI command, setting or migration. Verify duplicate callback suppression, independent names, normal/exception exit, abandoned ownership and simultaneous separate-process startup, alongside existing tray/control regression. Actual corrected installation remains a separate gate.

Test exact-byte snapshot/staleness with preserved timestamps, missing/unreadable/current-file states, no exposed token/hash/query/secret data, owner-mode probe avoidance, read-only signing metadata and mode distinctions, Project missing/disabled Provider diagnostics, MCP/CLI schema and control-pipe parity. Test tray command selection/state formatting with fakes, and parse/build both localization resources. Publish affected MCP/CLI/tray candidates only after tests; installed host/restart/GUI acceptance and shared CR closure require separate evidence. Existing normal settings/profiles/keys are retained.
# Tray settings and startup updates

The Windows tray is the interactive owner of startup updates. The MCP process remains usable through the existing headless command line. An MSI-installed tray checks the official Moyai release feed only when the host is stopped. It accepts a newer stable four-part version, an exact x64 asset name and URL, a matching size and SHA-256, and a valid Akatsukisoft publisher signature. A separate user process waits for the tray to exit, invokes Windows Installer, records the result, and relaunches the tray without another update check. This keeps update failures and Windows elevation separate from MCP requests.

The settings dialog edits only the startup update preference and Provider timeout in the canonical configuration. Saving checks for concurrent edits and replaces the file atomically. The timeout follows the existing explicit host restart rule. Automatic updates default to enabled and can be disabled. The CLI host control path remains available for headless operation.

