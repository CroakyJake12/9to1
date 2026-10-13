# Private Dulche preview transport

This is the source for the separately protected runtime relay and the proposed
preview API worker. The browser keeps the shared ChatSessionService and canonical
account-scoped conversation storage. The relay transports the existing Ollama
`/api/tags` and `/api/chat` wire protocols; it does not invent models, responses,
usage, accounts, task admission, or completion frames.

## Actual topology and authorization

The 13 October 2026 managed execution environment permits outbound HTTPS through
its supported proxy but grants no TCP destinations to cloudflared. The owner
approved a dedicated HTTPS long-poll/chunk relay as the alternative. The unused
staged tunnel is not the live transport and must not be described as connected.

The protected preview calls `https://dulche-runtime-20261013.9to1.uk` with its
server-only Access service credential and the actual browser CAKE bearer. The
runtime worker verifies the Cloudflare Access JWT against its issuer and exact
audience. Its Durable Object verifies the CAKE bearer through the maintained
account service, including before returning each output chunk. The HTTPS
connector independently checks that same actual account before dispatching the
real loopback Ollama request. No provider or service credential is sent to the
browser. Ollama remains at `127.0.0.1:11434` and exposes no public port.

Runtime resource identities:

- Worker: `9to1-dulche-runtime-20261013`.
- Access app: `24e402ea-5582-425d-a38d-6438ec2e401a`, service-only policy
  `f58ebd5f-8aa7-4594-bd7b-679aaa72eae6`.
- Durable Object binding `RUNTIME_RELAY`, class `RuntimeRelay`, namespace
  `463f7a2caf9845b9a17429e50a2109ba`.
- Account service binding `ACCOUNT_AUTH`: `cake-id-release-validation`.
- Approved service-token ID: `3b669525-bf42-49bc-a71b-56888877495c`;
  expiration **2026-10-14 19:33:03 UTC**. Secret values are external to source.
- Compatibility date `2026-10-08`, explicit `enable_request_signal`.
- Worker logs and observability are disabled; workers.dev and alternate preview
  URLs are disabled. Existing preview hostname and its owner Access policy are
  separate and unchanged by this resource.

## Build and operation

Use Node 24, `npm ci`, `npm test`, and `npm run build`. The exact dependency
versions are locked. Upload `relay-worker.bundle.mjs` as the module part named
`relay-worker.mjs`, using `relay.metadata.json`. The initial migration creates the
SQLite namespace; omit `migrations` on subsequent unchanged-schema deployments.
The SQL tables are additive and store only opaque job IDs, connector lease IDs,
and terminal acknowledgment time, never prompts, tokens, or generated content.

The test entry deliberately substitutes the outer service gate and account
protocol inside an isolated Workerd instance. **Never deploy
`relay-test-entry.bundle.mjs`.** Its results are not authenticated production
acceptance. Normal source deployment always uses `relay-worker.bundle.mjs`.

Run `relay-connector.mjs` with the public values in `connector.env.example` and an
external mode0600 `RUNTIME_SERVICE_CREDENTIAL_FILE` containing the actual
`client_id` and `client_secret`. Use the environment's approved HTTP proxy when
required. The connector logs only safe status counters. SIGINT/SIGTERM fences and
joins original operations. A separate 120-second connector deadline survives
loss of the relay's in-memory timer.

Four original response owners, including completed responses not yet released,
limit admission. A request body is limited to 1 MiB, each sequenced chunk to
64 KiB, total output to 8 MiB, and generation to 120 seconds. A claimed original
must acknowledge settled cleanup before cancellation can succeed. Failure or a
missing acknowledgment persists uncertainty and refuses new work. Restarted
Durable Objects recover original opaque custody and require the original
connector lease's acknowledgment; a replacement connector cannot clear it.
Completed receipts are released by the owning response after EOF or joined
cancellation, with no arbitrary receipt expiration.

## Existing preview integration

`preview-worker.mjs` is a module for the existing preview worker, not a second
site. Preserve its uploaded WASM assets, SPA handling, exact hostname, Access app,
COOP `same-origin`, COEP `require-corp`, and `Referrer-Policy: no-referrer`.
Add `enable_request_signal`, the `ASSETS` binding, the real `ACCOUNT_AUTH` service
binding, and these public values: `PREVIEW_ORIGIN`, `ACCOUNT_ORIGIN`, `DULCHE_ORIGIN`,
`MODEL_ID`. Supply `RUNTIME_ACCESS_CLIENT_ID` and
`RUNTIME_ACCESS_CLIENT_SECRET` as server secret bindings through the authorized
private deployment channel. Never commit or print them.

Set asset routing `run_worker_first` to `["/api/dulche/*"]`; other routes continue
through the existing assets. The only runtime routes are GET
`/api/dulche/api/tags` and POST `/api/dulche/api/chat`. The account bearer, origin,
model, request limits, and original stream custody are checked before dispatch.
Manual upstream redirect mode plus explicit unsuccessful-status rejection avoids
forwarding credentials through redirects and is supported by actual Workerd.

A new preview version must be staged with the exact compiled asset completion
receipt, compared against the previous version, and smoke-qualified before the
existing preview traffic is switched. Publishing is not product acceptance.

## Evidence and remaining gates

On 13 October, actual protected HTTPS rejected public requests401, admitted the
approved service credential, and still refused inference without a real CAKE
bearer401 once the connector was running. An actual HTTP abort released the
original Worker-to-DO connector long-poll; its replacement acquired the same
slot and completed normally after20.23seconds. Local Miniflare's HTTP stream
bridge did not propagate the equivalent browser abort, so that local probe is
retained as failed/inconclusive, not counted as passing production inference.

Canonical tests cover held-body capacity, sequence checks, late-byte authority
revocation, quarantine of already-admitted held bodies, cleanup failure, exact
completed receipts, foreign ID refusal, and actual persisted restart recovery.
Preview-wrapper controls use real Workerd or controlled fetch sources as stated
in their receipts. They do not establish a signed-in owner session.

**Still required:** a legitimate signed-in production Chat send, authentic model
response, Stop while streaming with original model shutdown acknowledged, reload
and conversation reopen, account change, and recovery of an abandoned outer
response owner. No full Spaces, authenticated inference, or unattended service
availability acceptance is claimed here. The managed host and 24-hour credential
are temporary operational dependencies. A retained uncertain or unreleased
original owner may deliberately stop admission; do not erase its records to
simulate successful cleanup.

## Rollback and recovery

Keep the previous compiled preview version and its assets. Roll back preview
traffic to that recorded version first; do not edit its hostname, owner Access
policy, account data, or IndexedDB schemas. Fence new relay admission and stop
the connector with SIGTERM, then verify its actual original operations have
joined. Do not treat sending an abort or a timeout as proof of closure.

The dedicated runtime hostname can then be detached from this worker; revoke
only this runtime service token if the connection is retired. Leave recorded
uncertain custody intact until its original connector provides the exact settled
acknowledgment. There is no public administrative endpoint that clears it.
Once the HTTPS replacement is qualified, the unused staged tunnel
`b78339e9-1012-42f5-858f-3227522a8c7f` may be removed separately. Its credential was
rotated before any connection or DNS activation. No rollback step removes account
storage or changes the existing preview Access app.
