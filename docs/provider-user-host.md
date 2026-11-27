# Provider user-host connections

## Githubie Windows owner connection

Githubie owner mode is an explicit selection. Configure the Provider entry with `name: githubie`, `toolPrefix: github`, `repository: true`, `authenticationMode: windows_owner`, `connectionMode: githubie_user_host_v1`, and `runtimeExecutable` as the absolute path of the selected `Githubie.Server.exe`. Moyai verifies that the discovery record names this path, that the running process image is this path, and the process owner, session, liveness, profile and file ACLs; it does not compare the executable's content hash, so a Githubie update at the same path keeps working (CR-2026-11-27). `runtimeSha256` is deprecated: an existing value is accepted and ignored. Omit `endpoint` and `runtimeDirectory`. The current Windows user's Githubie profile is resolved under the LocalApplicationData known folder; credentials are not placed in configuration. See [ADR 0023](adr/0023-same-user-provider-trust.md).

The Consumer checks the private profile, discovery schema, owner/session/process, executable path and pinned hash, profile hash and CurrentUser DPAPI before sending both session and generation headers. It checks Provider version, owner contract, registered tools and applicable capability before dispatch. It does not use signing assertions or static fallback. Mutations with an uncertain result are not replayed. Isolated real-Provider acceptance is still pending.

## Buckettie user-host connection

This is an opt-in Consumer for Buckettie discovery schema 1. It does not enable or migrate a normal Provider connection. Design and trust conditions: [ADR 0020](adr/0020-provider-user-host-consumer.md).

```json
{
  "name": "buckettie",
  "toolPrefix": "bitbucket",
  "repository": true,
  "connectionMode": "buckettie_user_host_v1",
  "runtimeDirectory": "<approved absolute Buckettie runtime directory>",
  "expectedMcpPath": "/mcp",
  "assertionToolScopes": {
    "bitbucket_repository_status": ["repository.read"]
  }
}
```

Replace the placeholder with an explicitly approved directory owned by the current interactive Windows user. Omit `endpoint`; no URL fallback is permitted. Provider configuration is trusted administrative input, not a tool argument. Existing Provider settings default to `static`. A configured discovery mode for another Provider is rejected.

The directory must contain the Provider's private `host.json`. The Consumer checks schema/version, Provider/profile, ACL/reparse points, owner/session and live process generation before using CurrentUser DPAPI. All HTTP requests carry the discovered owner credential. Signed tools additionally use the existing Project-bound assertion policy. No token is entered or copied into configuration.

| Error | Meaning |
|---|---|
| `user_host_not_running` | Runtime/discovery is absent. |
| `host_discovery_invalid` | Record, credential or endpoint is malformed. |
| `host_discovery_version_unsupported` | Discovery version is unsupported. |
| `host_provider_mismatch` | Record belongs to a different Provider. |
| `host_profile_mismatch` | Record belongs to a different runtime directory. |
| `host_owner_session_mismatch` | Owner or interactive session differs. |
| `host_discovery_stale` | Process has exited or its PID generation differs. |
| `host_discovery_unavailable` | ACL, access, IO or decryption prevented a trusted read. |

Read-only connection recovery is bounded to one retry. A mutation is never automatically resent; when its outcome is unknown, inspect Provider audit/state before deciding what to do. Starting, stopping or repairing the Provider remains the Provider owner's operation. Isolated interoperability is still pending; passing mocked transport tests alone is not delivery acceptance.
