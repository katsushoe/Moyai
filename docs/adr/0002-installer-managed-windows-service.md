# ADR 0002: Installer-managed Windows service

## Status

Service installation remains accepted. Configuration and CLI ownership are superseded by [ADR 0003](0003-service-owned-state-and-cli.md). Live installation, upgrade, uninstall and reboot validation remain required before release.

## Context

Users require startup when Windows boots, without logging in or manually registering the MCP executable. Previously the MSI only deployed files and the MCP host required process environment variables.

## Decision

Use the .NET Windows service lifetime and WiX ServiceInstall/ServiceControl. Register `Moyai` as automatic, start during installation, stop before replacement/removal, and remove service registration on uninstall. Keep interactive execution supported. Use LocalService and grant inherited modify access only to the existing data/log directories. Keep the database outside packaged files. Supply the fixed install data path and saved loopback URL as explicit arguments; persist the URL independently of executable removal.

## Alternatives

- A logon Run entry does not satisfy boot-time startup before login.
- A scheduled task duplicates lifecycle management and does not integrate service stop/start with MSI replacement.
- LocalSystem grants unnecessary privileges to build/deploy operations.

## Impact and security

The service does not inherit the user's credentials, environment, mapped drives or project permissions. External Providers or administrator-scoped access grants are required for operations outside its accessible directories. No tokens are placed in MSI or service arguments. Existing database files remain; protected ACLs require a migration check. The service uses an existing Application event source to avoid granting event-source creation privileges. Fatal errors are non-interactive. Loopback HTTP(S) validation applies to both execution modes.

## Operations

Stop a manually running MCP before installing to avoid a port conflict. MSI startup failure must fail installation. Registry configuration survives uninstall; repair/upgrade reads it before regenerating service arguments. Major upgrades require a newer version. Do not install an unreleased development MSI over the live instance without authorization.

## Implementation, tests and documentation

Implementation: `src/Moyai.Mcp/Program.cs`, `McpHostSettings.cs`, and `installer/Moyai.wxs`. Tests cover configuration validation/precedence, console lifetime compatibility and MSI service authoring. Build and inspect the generated MSI, then validate install/upgrade/uninstall/reboot on an isolated machine. `CONFIG.md` and `CONFIG.ja.md` define configuration and operation details.

## References

- [Microsoft: Host ASP.NET Core in a Windows Service](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-8.0)
- [WiX ServiceInstall](https://docs.firegiant.com/wix/schema/wxs/serviceinstall/)
- [WiX ServiceControl](https://docs.firegiant.com/wix/schema/wxs/servicecontrol/)
