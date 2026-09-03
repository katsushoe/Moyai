# PACKAGES.md Version
2026.09.03

# 変更履歴
- 2026.08.28
- 2026.09.03 Service-connected CLI and Windows SCM package.

# Moyai Package Inventory

This document is the source of truth for package references and update verification.

# Target Projects

Production and test targets are `net8.0` and `net8.0-windows`.

# Package Sources

Packages restore from the configured public NuGet source. No private feed or credentials are required.

# Direct Package References

| Package | Version | Purpose |
| :--- | :--- | :--- |
| `Microsoft.Data.Sqlite` | `8.0.12` | SQLite persistence |
| `Microsoft.Extensions.Http` | `8.0.1` | Provider HTTP clients |
| `Microsoft.Extensions.Hosting.WindowsServices` | `8.0.1` | Windows service lifetime and event logging |
| `System.ServiceProcess.ServiceController` | `8.0.1` | CLI service lifecycle management |
| `ModelContextProtocol` | `2.2.0` | MCP client transport |
| `Tomlyn` | `2.10.1` | TOML client configuration parsing and comment-preserving model serialization (BSD-2-Clause) |
| `ModelContextProtocol.AspNetCore` | `2.2.0` | MCP HTTP server |
| `Microsoft.NET.Test.Sdk` | `17.8.0` | Test host |
| `xunit` / `xunit.runner.visualstudio` | `2.5.3` | Unit tests |
| `coverlet.collector` | `6.0.0` | Coverage collection |

# Transitive Package References

Transitive dependencies are resolved from direct references and are not added directly unless product code requires their public API.

# Update Rules

Keep MCP packages on the same version. Review release notes and licenses, then restore, format, build, test, and package before accepting updates.

# Verification Commands

```powershell
dotnet list .\Moyai.slnx package --include-transitive
dotnet restore .\Moyai.slnx
dotnet build .\Moyai.slnx --configuration Release --no-restore
dotnet test .\Moyai.slnx --configuration Release --no-build
```
