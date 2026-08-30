# Changelog

[English](CHANGELOG.md) | [日本語](CHANGELOG.ja.md)

## 1.0.6 - 2026-08-30

- Added the Release domain model, SQLite persistence, application service, and matching CLI/MCP operations.
- Completed Repository Provider error normalization for retryable and policy failures.
- Aligned Project lookup, duplicate registration, and update targeting with ordinal case-insensitive comparison.
- Added MCP project discovery guidance, the `list_projects` tool alias, and registered-project candidates in not-found errors.

## 1.0.5 - 2026-08-29

- Repackaged version 1.0.4 without product behavior or database format changes.

## 1.0.4 - 2026-08-29

- Added SQLite schema v4 with relational constraints, revision contracts, and FTS5 synchronization.
- Added WorkItem relations, comments, Hataori task links, commit links, search, Project Overview, and Changes Since operations.
- Expanded the Repository Provider contract with capability, branch, and tag operations and normalized Provider errors.
- Added matching CLI and MCP operations, tests, and user documentation.
- Existing v1.0.3 databases migrate automatically with a backup created before migration.

## 1.0.3 - 2026-08-28

- Added repository URL and Provider updates through the Project update command and MCP tool.
- A repository URL change can infer its Provider again, while an explicit Provider overrides routing.
- Clarified that Moyai v1 manages one Repository association as part of each Project rather than through independent register/unregister commands.
- No database format changes from `1.0.2`.

## 1.0.2 - 2026-08-28

- Reorganized public documentation to follow the project document standard.
- Added paired English and Japanese entry, configuration, command, and MCP setup documents.
- Added package inventory, security policy, and canonical changelog documents.
- No product behavior or database format changes from `1.0.1`.

## 1.0.1 - 2026-08-28

- Added public user documentation.
- No product behavior or database format changes from `1.0.0`.

## 1.0.0 - 2026-08-28

- First stable release with CLI, Streamable HTTP MCP, SQLite state, provider routing, service authentication, and lifecycle operations.
