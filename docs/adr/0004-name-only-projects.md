# ADR 0004: Name-only Projects

## Status
Accepted. Project identity does not require a local repository or deployment configuration.

## Context
Requiring source paths and build/deploy settings prevents creating a Project for work tracking alone. Repository discovery and Project identity are separate operations.

## Decision
`project_create` requires only a name. All existing configuration arguments remain optional. `project_ensure` creates a missing Project by name or returns the existing record unchanged, including archive state. Concurrent requests use the existing unique name constraint and recover the winning record; only one creation event is stored. Read-only operations do not create records implicitly.

`project_configure` associates optional execution settings later, with required expected revision. Omitted/null settings retain their values. Empty strings represent unconfigured fields; install path may be null. This retains the existing SQLite schema and existing Projects without migration. Explicit empty settings clear string fields. Invalid nonempty deployment modes and uninferable nonempty repository URLs remain errors.

Repository, build and deployment execution validate the settings they need before contacting Providers. Work tracking and metadata queries do not require those settings. Provider authentication and approval rules remain unchanged.

## Alternatives
Automatically discovering a checkout or inventing deployment defaults was rejected because creating a Project does not identify either. Implicit creation by read-only queries was rejected because it changes their side-effect contract.

## Security and operations
Both CLI and MCP delegate creation/configuration to the service. No filesystem scan, environment variables, Provider registration or token creation is performed. Actor defaults are `client`/`unspecified`, indicating unavailable caller attribution rather than an authenticated identity; callers can supply explicit audit labels.

## Validation and documentation
Verify name-only persistence, work tracking, idempotence, concurrent creation, archive preservation, configuration updates/revision conflicts, and execution-time errors. CLI commands are `project-create`, `project-ensure`, and `project-configure`; MCP exposes their underscore equivalents with the same schemas.
