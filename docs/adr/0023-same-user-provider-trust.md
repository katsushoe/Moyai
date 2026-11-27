# ADR 0023: Trust applications owned by the same Windows user

## Status

Trust policy accepted by the product owner on 2026-10-25: applications running as the same Windows user are trusted. Buckettie's wire/error/acceptance contract is agreed and its Consumer is implemented in source. Githubie's owner transport is under implementation and its isolated interoperability and rollout remain pending. This decision does not describe the installed runtime as signature-free.

## Context

The user-session migration removes the LocalService identity boundary. Existing assertions independently authenticate Moyai, Project UUID, Repository and operation scope. The product owner now chooses the Windows user as the trust boundary for Githubie and Buckettie, rather than distinguishing each application or Project by signed assertions.

Before this change, ProviderRoutingService interpreted unsigned calls as legacy authentication; PR, branch and Settings paths required assertions, and release lifecycle had its own signing path. The explicit owner request now distinguishes this trust decision from legacy calls. Githubie's Consumer discovery integration remains separate work. An ordinary loopback URL does not authenticate a Windows user.

## Decision

- Target Githubie/Buckettie Repository operations and their release lifecycle calls. Once enabled against a supported Provider, authenticate the Windows owner and omit ES256 assertions. Do not require an Issuer, signing-key distribution, Project binding or assertion scopes to authorize these calls. Keep Project/Repository selection and correlation identifiers as routing/audit context, not a signed security identity.
- Use each Provider's documented authenticated user-host discovery or equivalent owner-authenticated local IPC. Validate owner SID, private discovery/credential ACL, intended Provider/profile and current process generation before sending a credential. Retain existing session and endpoint consistency checks. Being on localhost or knowing a port is insufficient.
- Trust applications that can authenticate as that Windows owner. Do not introduce application-specific trust lists or recreate Project-scoped delegation grants under another name. Provider-owned Repository/branch allowlists, operation capability checks, release approval and audit remain in force.
- Keep Moyai's inbound HTTP authentication and host-control ownership checks. Keep GitHub/Bitbucket credentials inside their Providers. This decision does not remove local secret encryption or authorize deletion of existing DBs, keys, trust registrations or tokens.
- Represent owner authentication distinctly from assertion and legacy modes. An unsigned request must not be treated as legacy, and legacy service tokens are not a substitute for owner authentication. Select the mode explicitly per Provider during migration; do not change unrelated Providers, including KelpieSSH.
- Require owner-authenticated capability/contract confirmation before dispatching business operations. Existing generic operation flags alone do not prove that unsigned owner-authenticated calls are supported. Missing/unsupported support, owner mismatch or rejected credentials must fail without switching to static endpoints, signatures, legacy tokens or unauthenticated HTTP.
- Agree on exact configuration values, capability fields, headers, error codes and version boundaries with each Provider owner before adding a production wire contract. Provisional labels in a CR are not implemented settings. Mutations with an unknown outcome are not replayed.

## Implementation and rollout

1. Confirm Githubie/Buckettie owner discovery, unsigned-operation coverage, native/Moyai route behavior and capability/version evidence. Complete the separate Githubie Consumer transport work or an agreed equivalent.
2. Introduce explicit owner-authenticated routing in Moyai's configuration, Application requests, Repository adapters and release lifecycle. Remove assertion-only gates for this selected mode after equivalent owner validation; retain input validation and Provider policy checks. An owner-only deployment must not need an active signing key solely for these operations.
3. Verify dedicated fixtures with no assertion Issuer/key or legacy token: read, commit, push, branch, Settings, PR and release operations where supported. Confirm wrong owner/missing or rotated credential, stale process, unsupported capability and redirect rejection; preserve policy refusals, audit, secret redaction and mutation non-replay.
4. Use an explicit backed-up configuration switch and install acceptance after the three implementations agree. Keep the current operational mode until then. Retire obsolete signing requirements only for migrated Githubie/Buckettie connections; preserve unrelated signing/encryption users and historical evidence.

## Alternatives

- Continue Project-bound signatures for these trusted applications: not selected by the product owner.
- Remove Authorization headers from existing static HTTP calls: rejected because this does not establish the caller's Windows owner or provide compatible Provider authorization. Use the verified owner transport instead.

## Ownership

Moyai owns its Consumer, configuration, routing and documentation. Githubie and Buckettie own their authentication boundaries and operation policies. Coordination uses separate CRs; this ADR does not authorize edits or tests in those repositories. The human owner explicitly authorized dispatch on2026-10-25. Both Provider owners have responded; Buckettie's Consumer source is implemented, while Githubie's Consumer and cross-product acceptance remain pending.

## Buckettie contract review on 2026-10-25

Moyai reviewed Buckettie's owner-authentication-contract.md proposal and agreed to its core wire, replying through Itoguruma af89abe3c5824e21b7fff40472b781a1. Proposed Provider version is at least2.0.1.0; this is not a release or installed-runtime claim.

- Provider configuration explicitly selects user_host.moyai_integration=true and user_host.moyai_authentication_mode="windows_owner"; existing ES256 configuration remains selected until a concrete switch.
- Preserve schema1 and X-Buckettie-Session on all HTTP. Omit Authorization, Issuer keys and legacy service tokens in owner mode.
- Require data.authentication.provider_id="buckettie", authentication_mode="windows_owner", numeric owner_protocol_version=1, boolean requires_assertion=false, boolean project_binding_required=false and integration_mode="moyai". Require both the generated supported_tools entry and the operation's implementation capability. Missing/wrong-type/unknown values and standalone mode are not substitutes.
- Owner mode does not load Issuer/Trust/replay DB/Project binding as a startup prerequisite. Project and X-Moyai-Operation-Id remain non-authorizing context. Authorization mixing is rejected rather than ignored.

The Provider's final error table and O01–O18 acceptance reconciliation were reviewed. Credential401 and loopback/Origin403 JSON errors, mixed-authentication/unsupported-tool401 JSON-RPC error.data and busy503 error.data prove no dispatch only with their exact documented code and outcome=not_executed. Communication loss after business dispatch remains unknown and is never replayed. Bootstrap calls are tracked separately from business dispatch.

Moyai selects `authenticationMode="windows_owner"` per dynamic Buckettie Provider. Application routing skips legacy-token lookup and assertion-expiry retry; Repository and release adapters do not resolve an Issuer. Every connection verifies get_version, contract3, explicit owner requirements, supported_tools and applicable operation flags before business dispatch. Only classified reads may reconnect once. Existing signed/legacy paths and unrelated Providers remain unchanged.

The dedicated tools/Moyai.OwnerConsumerAcceptance console creates a new private DB/Project without signing services and requires a fresh matching Provider ready record before HTTP. It pins the ready generation and reserves each explicit mutation case before dispatch to prevent rerun after a lost response. Provider owners retain fixture lifecycle and gateway approval. Normal runtime/profile/configuration and old signed fixtures remain untouched; old evidence does not establish owner-mode acceptance. Actual read/local mutation and approved remote PR/release acceptance remain open.

## Dedicated case verification

The acceptance console uses a nine-field Buckettie FixtureSettings DTO, including pull_request_routes, rather than assuming the public settings contain registration paths or HEAD metadata. It validates exact field types and compares all agreed values. Candidate, dependency manifest and exact case JSON are pinned before a fresh time-bounded ready; its approved_case_sha256 binds the admitted case ID to its full input bytes.

Mutation execution and acceptance are separate. Before dispatch, reserve the case permanently and create a global pending record. A valid returned data.commit_hash is saved but does not by itself establish accepted Git state. The Provider owner exports independent HEAD/parent/branch/ref/file observations after the response; the Consumer's offline verify command matches that evidence and clears pending only after a full comparison. This keeps Git observation with the Provider instead of bypassing Moyai/Githubie/Buckettie policy through raw Git in the Consumer. Unknown, crash, stale/mismatched evidence and unexpected results block subsequent cases; they do not authorize retry or cleanup. Even expected push refusal/branch-switch results require independent reconciliation. Exit2 denotes verification pending, not success or retry permission.

Pure validation tests and synthetic CLI verification establish these control paths only. Real SettingsGet and each human-approved mutation still need new case/expectation agreement, fresh Provider readiness and actual resulting evidence. Earlier successful read fixtures/candidates remain archived separately.

## Githubie final detail agreement on2026-10-26

Reply3e2f81298c9b43f18b180ed2e3a1d168 accepts windows-owner-acceptance-v1.md and the fixed2.1.0.0 candidate's58-tool/20-boolean map after independently matching the acceptance document, operation map, supplemental/original manifests and MSI hashes. Retain both manifests. This verifies artifact identity, not all source entries, a rebuild or reproduction of Provider tests.

The exact403/X-Githubie-Error pairs carry empty bodies and return before next()/MapMcp; owner rejection precedes mixed-authentication rejection. Bare403, arbitrary RPC errors and dispatched transport failures do not prove no execution. Distinguish handler busy/conflict, policy/failed and unknown; conservatively mark uncertain dispatched GitHub network writes unknown even if the existing mapper defaults to failed. Only metadata gets the bounded rediscovery retry; mutations never replay. Parse negotiated JSON/SSE, RPC structuredContent and Provider ok/error.

Require the exact called tool in owner operations and registered tools/list, and only its defined ordinary boolean. Undefined fields are not false and do not justify invented capability names. The old nine cases are mapped to O-cases, replacing signature-negative tests with owner/mixed-authentication/capability checks. Real external successes require separately approved disposable targets. Candidate/test/DB/profile pins and run time remain before any Githubie fixture start; Consumer implementation and real interoperability are still incomplete.

## Githubie core contract review on2026-10-26 (preceding history)

Reviewed the proposed windows-owner-authentication-v1.md contract from7ec6507d26224e93b7697d2327fb4376. Moyai's reply873bbf9f941a45229ffedf266583b791 agrees the core: explicit user_host.authentication_mode=windows_owner with legacy default, minimum2.1.0.0, Githubie-specific schema1/13-field/DPAPI discovery, both X-Githubie-Session and X-Githubie-Generation on every HTTP request, omitted Authorization, retained Provider policy/approval and owner SID audit. No Issuer, Project binding or assertion scopes authorize this mode.

Require authentication.integration_mode=windows_owner/direct_connection=unrestricted and the exact typed owner_authentication v1 object, including supported operation tool names, plus ordinary operation capabilities. Buckettie's header/capability contract is not interchangeable. Requested the final error-envelope/dispatch-boundary table, full operation/capability map, pinned candidate/source hashes and old-ES256/new-owner acceptance reconciliation before implementation. HTTP403 alone does not prove no execution; mutations with unknown outcomes are never replayed. This is core contract agreement, not completed Githubie Consumer implementation, verified Provider functionality or rollout.
