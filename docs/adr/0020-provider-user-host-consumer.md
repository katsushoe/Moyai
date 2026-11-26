# ADR 0020: Provider user-host Consumer

## Status

2026-10-24: Consumer implemented in the working tree; focused local verification 135/135 passed. Buckettie owner agreed with the settings in message 9ad4ead302d24a2ea44c9acfdbfa72b5. A separate console fixture entry prepares a dedicated DB and CurrentUser-DPAPI-protected test key without using the normal host/control pipe. Provider ready/import and isolated interoperability remain pending, so this is not delivery acceptance.

## Context

Buckettie candidate 2.0.0.1 publishes discovery schema 1 in its approved runtime directory. A fixed URL cannot identify its current process generation or supply its per-start owner credential. The authoritative wire contract is Buckettie's `docs/user-host-discovery.md` and ADR 0022, requested by CR-2026-10-24-moyai-buckettie-user-host-consumer.

## Decision

Provider configuration defaults to `connectionMode: "static"`. Explicit `buckettie_user_host_v1` requires `name: "buckettie"`, `toolPrefix: "bitbucket"`, an absolute `runtimeDirectory`, and canonical `expectedMcpPath` (default `/mcp`). In this mode `endpoint` must be omitted or null. There is no fallback to a static endpoint, unversioned record, another Provider or another profile. Existing Githubie static connections retain their current behavior. Githubie's provisional discovery wire is not treated as this contract.

Before each connection, validate the bounded UTF-8 schema 1 document, duplicate names, Provider/profile binding, private ACL, owner SID and interactive session, live PID and precise UTC process start ticks. Reject reparse ancestors. Decrypt only with Windows CurrentUser DPAPI and the Buckettie entropy, then validate the process again. The Consumer never creates or repairs Provider directories, files or ACLs.

The credential is sent as `X-Buckettie-Session` on every HTTP request to the resolved endpoint, including bootstrap and initialization. Existing ES256 assertions remain additional authorization for signed tools. Credentials are held only for the connection and redacted from outputs; buffers are cleared. Redirects are disabled by the host's HTTP factory. The forwarding handler rejects a different endpoint and 3xx responses. A custom embedding host must provide an HTTP factory that also disables redirects and credential logging.

Read operations may resolve again at most once after a transport/authentication/stale discovery failure. Mutations are never automatically retried in this mode, including lifecycle assertion-expiry retries. Once a mutation tool request may have been sent, a transport failure is an unknown outcome unless an exact owner-gate rejection with `outcome: "not_executed"` proves no dispatch. Subsequent operations create new connections and discover the latest generation.

## Alternatives

- Fixed endpoint fallback: rejected because a stale or different process could receive credentials.
- Common inference from Githubie/Buckettie provisional formats: rejected because their versions, encryption envelopes and bootstrap rules differ.
- Automatically repair ACLs or reuse another session: rejected because Consumer reads must not change the Provider trust boundary.

## Impact, security and operations

Configuration, repository/bootstrap transports and assertion-based lifecycle transports use the explicit connection mode. Existing static configurations need no migration. Dynamic lifecycle calls require assertions. No credential is stored in Moyai configuration, DB, CLI arguments or messages. Normal installed settings, Services, Provider registrations and production keys are outside this CR. Buckettie fixture startup/shutdown remains its owner's responsibility.

## Implementation, tests and user documentation

`BuckettieUserHostResolver` owns wire/Windows validation; `ProviderConnection` owns credential forwarding/redaction; composition passes only public connection settings. Focused resolver/configuration/transport tests cover rejection, rotation, owner plus assertion, redaction and mutation non-replay. Real isolated interoperability is recorded separately and must complete before declaring the CR complete. See `docs/provider-user-host.md` for configuration and errors.
