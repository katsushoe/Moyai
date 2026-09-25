# ADR 0008: Provider Assertions and protected secrets

## Status

Implementation approved by the user on 2026-09-06. Installation, service updates, MSI creation and publication remain separate approval steps.

## Context

The [transferred specification](../specifications/Provider_Authentication_Specification.md) is the protocol authority. Static tokens currently authenticate repository mutations; all authenticated repository operations must instead be bound to one Provider, Project UUID, repository and minimal tool scope.

## Decision

Application defines signer, issuer, validator, replay and KEK interfaces. Infrastructure implements the shared ES256 protocol, SQLite replay/envelope storage and protected-key adapters. Provider adapters consume the shared validation principal rather than interpreting JWT independently. Trust is supplied through administrator-controlled public settings and checked on every request, including revocation. ES256 uses the 64-byte IEEE P1363 signature encoding required by [RFC 7518](https://www.rfc-editor.org/rfc/rfc7518#section-3.4); verification applies [RFC 8725](https://www.rfc-editor.org/rfc/rfc8725) algorithm and audience restrictions.

MCP discovery/initialization is a bootstrap boundary: assertions are attached only to tools/call, never reused across HTTP requests. Repository routing re-resolves authorization before at most one retry of a verified expired result; unknown outcomes and other failures never retry. No automatic fallback to legacy authentication is permitted.

## Alternatives

- Per-Provider JWT code: rejected because validation can diverge.
- Global HTTP default authorization header: rejected because initialization and execution would replay the same assertion.
- KEK in the database: rejected because database theft would expose all secrets.

## Impact and security conditions

Repository service tokens are a migration-only path with an explicit finite deadline. External-service credentials remain Provider-owned. Core and the envelope schema contain no OS APIs. Key-provider failures are closed failures and never select a plaintext fallback. Public settings and trust files require administrator protection. Replay state must survive process restarts and be shared by all Provider instances accepting the issuer.

## Operations, tests and documentation

Initialization and key rotation must be explicit. Trust distribution precedes active-key rotation; failed distribution must leave the active key unchanged. Validate audience, context, scopes, time, replay, tampering, rotation and transport isolation with the same common validator. Each Provider project owns its integration; platform adapters require native-platform verification before operational rollout. CONFIG and the transfer receipt track available configuration and unverified integrations.
