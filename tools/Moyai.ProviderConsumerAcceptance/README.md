# Dedicated Provider Consumer acceptance entry

This Windows console entry uses Moyai's Infrastructure directly with a separate SQLite database and a dedicated CurrentUser-DPAPI-protected test signing key. It never starts an MCP host, opens a host-control pipe, stops the installed host, or accesses the normal Moyai profile. It has no mutation command.

```powershell
dotnet build tools/Moyai.ProviderConsumerAcceptance/Moyai.ProviderConsumerAcceptance.csproj
dotnet tools/Moyai.ProviderConsumerAcceptance/bin/Debug/net8.0-windows/Moyai.ProviderConsumerAcceptance.dll prepare <new-absolute-private-profile>
```

Preparation contacts no Provider and writes only the new private directory: `fixture.db`, public `fixture-plan.json` and `public-trust.json`. Only public trust and the dedicated Project UUID may be sent to the Provider owner. The active test key is a local fixture key; `provider_distribution_confirmed=false` remains explicit until the owner reports import. No normal UUID/key is reused. Keep the private DB local; do not send it. The test key currently follows the ring's one-year validity and must be revoked/retired and the private fixture removed after acceptance.

After the Provider owner explicitly reports ready and its public record has been verified, place an acknowledged `provider-ready.json` in the fixture directory with these public fields. Do not create a ready record from a reserved path or infer readiness from a listening port:

```json
{
  "ready": true,
  "schema_version": 1,
  "provider_id": "buckettie",
  "project_uuid": "<dedicated UUID from fixture-plan.json>",
  "runtime_directory": "<agreed absolute Provider runtime>",
  "profile_id": "<exact lowercase runtime profile hash>",
  "public_trust_sha256": "<uppercase SHA256 of acknowledged public-trust.json>"
}
```

Then explicitly run:

```powershell
dotnet tools/Moyai.ProviderConsumerAcceptance/bin/Debug/net8.0-windows/Moyai.ProviderConsumerAcceptance.dll read <private-profile> --provider-ready-confirmed
```

`read` invokes bootstrap/version, capabilities and signed local repository status using the product Consumer. `verify` runs those positive reads first, then owner-authenticated `tools/list`, owner-only native direct-read success (no Authorization, matching Buckettie's existing read_only policy), wrong-scope signed-read rejection, valid assertion without owner rejection, and incorrect synthetic owner rejection. The native-read probe is a separate contract check; normal Moyai routed reads retain owner credentials plus ES256 and never fall back from a rejected assertion. Every tool request is local status/read; no Git mutation or remote API is called. It reports only whitelisted safe status/error codes, with HTTP timeouts and disabled redirects. The product Infrastructure grants this test assembly internal access to the existing resolver/connection for these probes; this console is not included in the installed product.

`rotation` retains the first validated generation and credential only in memory, emits `provider_restart_required`, and waits up to five minutes for the exact stdin line `provider-ready`. Keep stdin open (for example a terminal/PTY). The Provider owner must restart only its fixture and report ready before the operator writes that line. The console does not start/stop the Provider. It then resolves again, compares generation and credential without printing or persisting either, and performs the normal positive reads with a fresh connection. It never forwards the old credential to a new generation. Do not send a synthetic stdin confirmation before the owner's ready report.

All three connection modes reject omitted confirmation before accessing fixture state or connecting. Build and these guards can be checked while the Provider is stopped. Actual positive/negative/rotation acceptance still requires the real owner-managed fixture and has not been executed. Mocked/Windows-local results and a successful build remain distinct from live acceptance. Unknown-mutation non-replay remains covered by the product's scoped transport tests; this console has no live mutation probe.
