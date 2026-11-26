# ADR 0021: Four-part product versions in MSI upgrades

## Status

Accepted for the 2026-10-25 local candidate. This does not approve installation or public release.

## Context

Product and file versions use a.b.c.d. Windows Installer compares only three ProductVersion fields, so passing2.0.0.1 directly cannot distinguish it from installed2.0.0.0. Existing UpgradeCode and downgrade rejection must remain effective.

## Decision

Keep a.b.c.d in executable assemblies, filenames and release notes. Encode MSI ProductVersion as a.b.(c*100+d), with a/b <=255, d <=99 and the encoded final component <=65535. Reject out-of-range versions before publishing files; increase a higher component instead of truncating. All newly built packages use this mapping, including d=0, preserving order across revision rollover. Existing2.0.0.0 compares as2.0.0 and upgrades to2.0.1. Earlier1.x major/minor versions remain below2.0.

## Alternatives

- Ignore the fourth field: rejected because revision-only fixes cannot upgrade reliably.
- Allow same-version major upgrades: rejected because it would also permit revision downgrades.
- Change the product version to a feature increment: rejected because the change is a fix.

## Scope, security and operations

Only packaging metadata changes. UpgradeCode, profile/client transactional actions and legacy-data retention remain unchanged. This version conversion grants no permission to change profiles or install/publish. Candidates remain in a new artifacts directory until verification and authorized live acceptance.

## Implementation and verification

Get-MsiProductVersion.ps1 is the single conversion used by Build-Installer.ps1 and Test-UserHostPackage.ps1. Test-MsiProductVersion.ps1 verifies valid mappings, limits, malformed versions and rollover ordering. Verify the actual MSI Property/Upgrade tables and executable file versions, then verify live upgrade retention separately. English/Japanese changelogs describe the local candidate and internal MSI version.

Reference: [Microsoft ProductVersion property](https://learn.microsoft.com/windows/win32/msi/productversion).
