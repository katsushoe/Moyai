# User-scoped MCP client registration

## Status
Implemented in development; MSI lifecycle validation is required before release.

## Context
Installing the Windows service did not register it with Codex or Claude Code. A per-machine service installer must not infer the intended user's profile from its elevated service account.

## Decision
`moyaictl configure codex|claude` and `unconfigure codex|claude` own client configuration changes. They are local installation management commands, not service business operations. The MSI invokes the same commands under impersonation with an explicitly selected profile. The dialog offers separate client selections; silent installation requires explicit properties. Selection is saved for repair, upgrade and removal. Major-upgrade removal does not unregister the clients.

Codex uses `<profile>/.codex/config.toml`; Claude Code uses `<profile>/.claude.json`, in user scope. These default locations are explicit: custom client configuration roots are not inferred. Client installation is not required; selecting an absent client preconfigures it for subsequent installation. The service configuration supplies the endpoint. Tomlyn 2.10.1 parses and writes TOML; System.Text.Json handles JSON. Unrelated values and TOML comments are retained; formatting may be normalized.

## Ownership and recovery
Only a newly created Moyai entry is owned. An identical pre-existing entry is left untouched and is not adopted. A differing unowned entry is rejected. Owned entries can update their endpoint, but user modifications to the entry cause a conflict. Removal deletes only an unchanged owned entry. Ownership records under `<profile>/.moyai` contain the endpoint, not credentials.

Writes use flushed temporary files and atomic replacement. A per-client lock serializes Moyai operations. A pending journal contains before/after bytes for configuration and ownership; failed operations restore them. MSI rollback restores byte-exact original files, while commit deletes the journal. MSI sessions pass a transaction identifier so rollback cannot consume a prior interrupted transaction. Recovery refuses to overwrite concurrent changes. Close the selected clients during installation. Interrupted transactions can be completed with `client-transaction ... --phase rollback|commit` after review.

The MSI embeds a self-contained build of the same CLI as its custom-action binary. Commit/rollback actions therefore remain executable after installed files have been removed. The selection dialog is shown for initial installations; maintenance and upgrades retain the saved selection.

## Alternatives
- Running client executables from an elevated installer: rejected because they may be absent and may target the wrong identity.
- Machine-wide forced registration: rejected because clients use user-owned settings.
- Unconditional overwrite/removal: rejected because existing registrations may be user-managed.

## Security and operations
The profile must already exist and be absolute. Configuration paths cannot traverse reparse points. Custom actions impersonate the installing user; access to another profile still requires OS permissions. No client executable is launched. Journals can contain existing configuration secrets and remain inside the user's profile; do not publish them. The installer never requests tokens or changes client approval policy. Explicit client selections consent to registration. Uninstall uses saved selections/profile; for additional users, run unconfigure as each user before removal. Restart clients after configuration.

## Implementation and verification
`ClientRegistration`, CLI help, WiX actions/dialog, MCP_SETUP and CONFIG describe the same contract. Tests cover preservation, ownership, malformed inputs, idempotence, endpoint changes, rollback and uninstall. Package tests inspect action ordering and impersonation; isolated Windows MSI installation/removal validates the complete lifecycle. Product publication still requires explicit release approval.
