# Moyai

Windows packages provide `moyaimcp.exe` as the MCP command client alongside the compatible `moyaictl.exe`. Both use the same implementation and call the current user's running MCP host. The tray controller manages host state and logon autostart. Its MCP server submenu groups status, Check status, Start, and confirmed Stop, with green/yellow/red/gray pill LEDs for running/not responding/error/stopped. See [tray configuration](CONFIG.md#tray-mcp-server-menu) and [CLI usage](COMMANDS.md#mcp-command-client).

Projects can be created using only a name: `moyaictl.exe project-create --name Sample`. For automatic registration when missing, use `moyaictl.exe project-ensure --name Sample`; existing settings are preserved. Associate repository/build/deployment settings later with `project-configure --name Sample --expected-revision 1`. No source path is needed for Project creation or work tracking. See [the command contract](COMMANDS.md) and [ADR 0004](docs/adr/0004-name-only-projects.md).

[English](README.md) | [日本語](README.ja.md)

Development2.0.4.0 adds explicit [PR source publication create/status](docs/pr-source-publication.md), preserving partial outcomes and requiring status reconciliation without automatic mutation replay. This is separate from installed/live acceptance.

Moyai is a Windows tool for managing projects and work items in SQLite and delegating repository, build, release, and deployment operations to configured providers. It provides a JSON CLI and a stateless Streamable HTTP MCP server.

## Getting Started

Optional Buckettie discovery schema 1 connection settings and recovery rules are documented in [Provider user-host connection](docs/provider-user-host.md).

These user-session instructions describe the unreleased 2.0 development build. Published 1.x installers use the previous Windows Service model.
The2.0.3.1 installer requires .NET Framework4.8 for its embedded owner resolver. CLI/host/tray application runtimes remain self-contained.

Install the MSI, then run:

```powershell
& 'C:\Moyai\bin\moyaictl.exe' host start
& 'C:\Moyai\bin\moyaictl.exe' version
& 'C:\Moyai\bin\moyaictl.exe' project-list
```

The MSI initializes the signed-in user's profile and enables tray autostart for a new profile. Start or stop the host from the tray menu, or use:

```powershell
& 'C:\Moyai\bin\moyaictl.exe' host start
```

The endpoint and authentication token are stored only in `<INSTALLFOLDER>\config\moyai.json`; runtime data remains under `%LOCALAPPDATA%\Akatsukisoft\Moyai`; configure the MCP client through the MSI or CLI `configure` command. See [MCP Setup](MCP_SETUP.md).

## Installation

### Installer

Download `Moyai-1.0.7-x64.msi` and its `.sha256` file from [Moyai v1.0.7](https://github.com/katsushoe/Moyai/releases/tag/v1.0.7). Verify both SHA-256 values match, then run the MSI as an administrator. Moyai is installed in `C:\Moyai`.

### Binary Archive

A binary archive is not distributed. The WiX Toolset MSI is the supported Windows package.

### Source Build

Prerequisites are the .NET 10 SDK, including support for targeting .NET 8, and the repository-local WiX tool manifest.

```powershell
dotnet restore .\Moyai.slnx
dotnet build .\Moyai.slnx --configuration Release --no-restore
dotnet test .\Moyai.slnx --configuration Release --no-build
.\scripts\Build-Installer.ps1 -Version 1.0.7
```

## Configuration

Sole configuration: `<INSTALLFOLDER>\config\moyai.json`. See [CONFIG](CONFIG.md). CLI business commands connect to the current user's running host.

## Usage

The CLI writes successful JSON to standard output, structured errors to standard error, and returns exit code `0` for success or `1` for failure. See [Commands](COMMANDS.md) for every command, option, result, and safety condition.

## Documentation

- [Provider authentication and local secret protection specification (Japanese)](docs/specifications/Provider_Authentication_Specification.md)
- [Authentication specification transfer and remaining work (Japanese)](docs/specifications/provider-authentication-transfer.md)
- [Repository Provider Contract: Git remote resolution (Japanese)](docs/specifications/repository-provider-contract.md)
- [KelpieSSH Protocol v2 staged deployment decision](docs/adr/0009-kelpiessh-protocol-v2.md)
- [Configuration](CONFIG.md)
- [Commands](COMMANDS.md)
- [MCP Setup](MCP_SETUP.md)
- [Packages](PACKAGES.md)
- [Security](SECURITY.md)
- [Changelog](CHANGELOG.md)
- [v1 Completion Roadmap](ROADMAP.md)
- [v1 Acceptance Criteria Traceability](V1_TRACEABILITY.md)
- [Architecture decision](docs/adr/0001-initial-architecture.md)
- [Installer-managed Windows service decision](docs/adr/0002-installer-managed-windows-service.md)
- [Service configuration and CLI decision](docs/adr/0003-service-owned-state-and-cli.md)
- [User-session MCP host and per-user state](docs/adr/0019-user-session-mcp-host.md)

## Security

The MCP listener accepts loopback URLs only. Do not place service tokens in source files, command history, logs, or documentation. Review [Security](SECURITY.md) before enabling providers or lifecycle operations.

## License

Moyai is licensed under the [MIT License](LICENSE).
