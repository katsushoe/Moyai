# Moyai

[English](README.md) | [日本語](README.ja.md)

Moyai is a Windows tool for managing projects and work items in SQLite and delegating repository, build, release, and deployment operations to configured providers. It provides a JSON CLI and a stateless Streamable HTTP MCP server.

## Getting Started

Install the MSI, then run:

```powershell
& 'C:\Moyai\bin\moyaictl.exe' version
& 'C:\Moyai\bin\moyaictl.exe' project-list
```

The development MSI registers and starts a Windows service automatically; see [automatic Windows startup](CONFIG.md#automatic-windows-startup). The published v1.0.7 MSI predates this change. To start a stopped service:

```powershell
& 'C:\Moyai\bin\moyaictl.exe' service start
```

Register `http://127.0.0.1:43120/mcp` as a Streamable HTTP server in the MCP client. See [MCP Setup](MCP_SETUP.md) for complete client configuration.

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

Configuration: `config/moyai.json`. See [CONFIG](CONFIG.md). CLI business commands connect to the running service.

## Usage

The CLI writes successful JSON to standard output, structured errors to standard error, and returns exit code `0` for success or `1` for failure. See [Commands](COMMANDS.md) for every command, option, result, and safety condition.

## Documentation

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

## Security

The MCP listener accepts loopback URLs only. Do not place service tokens in source files, command history, logs, or documentation. Review [Security](SECURITY.md) before enabling providers or lifecycle operations.

## License

Moyai is licensed under the [MIT License](LICENSE).
