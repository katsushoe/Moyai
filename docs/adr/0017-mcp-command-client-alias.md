# ADR 0017: MCP command client executable

## Status

Accepted for source implementation on 2026-10-21. Version 1.3.14.0 passed package validation and 48 installed upgrade/uninstall/reinstall checks, preserving configuration and database contents. Combined regression runs passed 701 tests. Actual Provider approval-dialog interaction remains unverified; identical apphost/assembly routing preserves the existing conditional approval behavior.

## Context

Users need a recognizable MCP command client name while retaining the existing `moyaictl` contract. A separate command implementation would risk different argument handling, authorization and errors.

## Decision

Build and publish `moyaimcp.exe` as a byte-identical copy of the Windows `moyaictl.exe` apphost in the same directory. Both resolve the same CLI assembly and dependencies; single-file publishing copies the complete executable. No second application, command dispatch implementation or service host is introduced. The MSI's existing publish-file harvest includes the additional executable.

Both names accept `--config <path>` before or after the command. Business commands read `serverUrl` from that configuration and call the running service's MCP tools and schemas. Existing management commands retain their Windows SCM or local configuration responsibilities. Explicit `service start` remains a management operation. `--help` and `--version` run locally without loading a configuration or connecting; `version` continues to query the service.

## Alternatives

A second CLI project with duplicated dispatch was rejected because it would create two implementations of the same contract. A launcher process was rejected because it would add process forwarding and exit-code handling. Renaming the existing executable would break installed scripts and MSI actions.

## Impact, security and operations

The alias requires the CLI assembly and dependencies beside it for ordinary multi-file publishing. It inherits the same Project selection, timeout, approval, audit, structured failure and exit-code behavior. No endpoint, credential, Provider scope or service setting changes are introduced. Existing `moyaictl` commands remain valid. Building a candidate package does not authorize live installation or publication.

## Validation and documentation

Exercise both real apphosts against a disposable MCP server for successful and failed business responses, including structured and text results and leading/trailing configuration. Verify local help/version with a missing configuration. Confirm alias inclusion in published output and the MSI File table. Update English and Japanese command/setup documentation and the service package check. Live install, upgrade and uninstall remain separate Windows acceptance gates.
