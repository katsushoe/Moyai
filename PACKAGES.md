# PACKAGES.md Version
2026.11.13

# 変更履歴
- 2026.08.28
- 2026.09.03 Service-connected CLI and Windows SCM package.
- 2026.10.22 User-session host and tray controller; Windows Service dependencies removed.
- 2026.11.01 Installer owner resolution and utility ACL custom actions.
- 2026.11.13 Complete candidate input manifest and pre/post packaging verification.

# Moyai Package Inventory

This document is the source of truth for package references and update verification.

`scripts/Build-Installer.ps1` freezes `delivery-inputs.json` in the selected artifact directory before publishing, then verifies exact file membership, sizes and SHA256 after MSI packaging. Inputs include source/tests/tools/scripts/installer/docs, root build files and public documents, `.config` and `.github`, including `.resx`, `.config` and pinned tool versions. Generated/private directories (`bin`, `obj`, `.git`, `.local`, `artifacts`, `.wix`, client/agent settings, dependency caches and TestResults) are excluded; links are rejected rather than followed. `New-DeliveryManifest.ps1` never overwrites an existing manifest; `Test-DeliveryManifest.ps1` validates it. `package-source-link.json` binds the manifest hash and MSI hash.

This input list includes filesystem files regardless of Git tracking status. Its `gitStateComplete=false` is an explicit pre-commit gate: obtain a complete typed Provider inventory before selecting files to commit; do not interpret a capped status list as complete or silently add the whole tree. Existing frozen candidates are retained under separate artifact directories. Verification of source/package identity does not establish installed, Provider or public-release acceptance.

# Target Projects

Production and test targets are `net8.0` and `net8.0-windows`.
The embedded installer-only owner resolver targets `net48`; it runs before the installation transaction and requires .NET Framework4.8 on the installing Windows machine. It does not change the application runtime target.

# Package Sources

Packages restore from the configured public NuGet source. No private feed or credentials are required.

# Direct Package References

| Package | Version | Purpose |
| :--- | :--- | :--- |
| `Microsoft.Data.Sqlite` | `8.0.12` | SQLite persistence |
| `Microsoft.Extensions.Http` | `8.0.1` | Provider HTTP clients |
| `ModelContextProtocol` | `2.2.0` | MCP client transport |
| `Tomlyn` | `2.10.1` | TOML client configuration parsing and comment-preserving model serialization (BSD-2-Clause) |
| `ModelContextProtocol.AspNetCore` | `2.2.0` | MCP HTTP server |
| `Microsoft.NET.Test.Sdk` | `17.8.0` | Test host |
| `xunit` / `xunit.runner.visualstudio` | `2.5.3` | Unit tests |
| `coverlet.collector` | `6.0.0` | Coverage collection |
| `WixToolset.Dtf.CustomAction` | `5.0.2` | Package the read-only owner-SID resolver as an MSI DLL custom action |
| `WixToolset.Util.wixext` | `5.0.2` | Pinned build-time WiX extension; formatted owner ACL application and native ACL rollback |

# Transitive Package References

Transitive dependencies are resolved from direct references and are not added directly unless product code requires their public API.
The installer custom-action package brings `WixToolset.Dtf.WindowsInstaller`5.0.2; SDK reference assemblies for `net48` are build-only. The utility extension is restored into the local ignored WiX cache by the installer build script.

The tray controller uses the .NET 8 Windows Forms framework reference supplied by the Windows Desktop SDK; it adds no direct NuGet package.

The installer publishes the CLI into `bin`, the MCP host into `bin/host`, and the tray into `bin/tray`. Each executable retains its own runtime and dependency files; do not merge the publish outputs because their transitive framework package versions can differ. The embedded MSI helper is published separately as a self-contained single file. `Build-Installer.ps1` checks both published CLI names and the embedded helper with `--version` before packaging. NuGet auditing is enabled by default; `-SkipNuGetAudit` explicitly skips audit-data retrieval for an offline build and does not establish that dependencies have passed a vulnerability audit.

# Update Rules

Keep MCP packages on the same version. Review release notes and licenses, then restore, format, build, test, and package before accepting updates.

# Verification Commands

```powershell
dotnet list .\Moyai.slnx package --include-transitive
dotnet restore .\Moyai.slnx
dotnet build .\Moyai.slnx --configuration Release --no-restore
dotnet test .\Moyai.slnx --configuration Release --no-build
```
