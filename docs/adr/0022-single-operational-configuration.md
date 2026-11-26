# ADR 0022: One operational configuration file

## Status

Accepted by the product owner's2026-10-25 instruction to use `<INSTALLFOLDER>\config\moyai.json` as the only configuration file. Supersedes ADR0019 only for operational configuration location and ownership; runtime data, logs and private signing keys remain user scoped.

The owner clarified that the installation directory is selectable. `<INSTALLFOLDER>` is the actual selected installation directory, not a hardcoded drive or folder. The initial fixed-root candidate is superseded and must not be deployed.

## Context

The user-profile default created ambiguity with a retained legacy Service configuration at the installation path. The existing installation-path file has old connection values and no current HTTP token. Selecting it without promotion would change identity/endpoint or fail authentication.

## Decision

- CLI, host and tray operations use `<INSTALLFOLDER>\config\moyai.json`. MSI stores the actual directory in the 64-bit HKLM product registration (`InstallDirectory`), so its extracted setup helper uses the same location. Without an MSI registration, packaged CLI/host/tray layouts resolve the root from their executable directory. Invalid registration fails explicitly. Existing --config syntax accepts only a normalized reference to that same file. No environment/profile configuration fallback is introduced. Offline help/version never load a configuration.
- MSI offers a directory input and accepts `INSTALLFOLDER` in silent installation. The default applies only when no directory was selected or retained. Repair/upgrade restores the registered directory. Profile initialization follows the registry write, so it cannot resolve configuration under the helper's temporary extraction directory. Directory selection uses the documented [SetTargetPath event](https://learn.microsoft.com/en-us/windows/win32/msi/settargetpath-controlevent) during fresh full-UI installation.
- Host start/restart validates this configuration before process changes and does not silently initialize missing configuration. Explicit config-init and installer setup can create the canonical file.
- Preserve current endpoint, token, Provider settings, database path, issuer and key namespace during an explicit one-time promotion. Back up both the legacy installation-path file and active user-profile file privately before replacement. Retire the obsolete operational file after successful acceptance; do not import legacy DB/key material.
- Keep one configured Windows owner. MSI persists ConfigOwnerSid, grants that SID modification access plus SYSTEM/Administrators, and gives no broad Users or legacy LocalService access to the secret-bearing configuration directory. Repair retains the configured owner. A second user must not silently receive this owner's endpoint/token or a separate profile configuration; multi-user shared configuration requires a future explicit design.
- Installer implementation correction2026-11-01: native `MsiLockPermissionsEx.SDDLText` cannot expand MSI public/private properties ([Windows Installer contract](https://learn.microsoft.com/en-us/windows/win32/msi/formattedsddltext)). Use a literal SYSTEM/Administrators protected base ACL, resolve the persisted owner SID to a round-trip-checked account name in an immediate DTF action before InstallInitialize, then append only that owner's inheritable Modify/Synchronize rights with the pinned WiX utility permission action. The resolver changes only an MSI session property, never files, registry or credentials; the native utility action handles ACL execution/rollback. A missing/unresolvable owner fails before old-product removal. Schedule major-upgrade removal after InstallInitialize so a later failure can restore the prior product. The rejected2.0.3.0 package and failed live evidence remain historical, not proof of installed acceptance.
- Generic settings parsing and isolated test/Consumer fixtures can still read explicitly supplied fixture documents; they are not production configuration-selection paths. Native CLI tests reject alternate paths, while HTTP/exit-code tests exercise the extracted internal MCP client with isolated settings and no operational-file mutation.

## Alternatives

- Change only the default and retain arbitrary runtime --config: rejected because this leaves several operational configuration sources.
- Load the legacy installation-path file unchanged: rejected because current authentication and registered state would be lost.
- Share the central bearer token with all Windows users or move the DB into the legacy data directory: rejected because the owner did not request new sharing or data/key migration.

## Scope and verification

The installer-only resolver uses the pinned DTF5.0.2 native x64 stub and a `net48` assembly. `Build-Installer.ps1` explicitly sets the DTF build platform and rejects a native PE machine value other than AMD64. Its startup configuration enables legacy v2 runtime activation for CLR4 hosting. Validate the embedded DLL's byte identity and run valid, invalid and missing-SID cases through a dedicated .NET Framework harness; PowerShell-hosted direct action calls on the observed machine waited without completing. The probe invokes only the resolver, not an installation sequence, and does not prove installed ACL behavior.

Update MoyaiSettings, both executable entry points, host control setup, MSI ACL/owner persistence, help and bilingual configuration/setup docs. Verify canonical normalization, alternative rejection before network calls, offline flags, missing-file failure, generic isolated parsing and real MCP business-response handling. Validate package permissions and perform a hash-guarded, backed-up promotion with process-exit confirmation and independent Project/key/config/endpoint retention checks. No release or unrelated Provider rights are authorized by this change.
