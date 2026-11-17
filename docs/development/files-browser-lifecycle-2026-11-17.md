# Files browser: original-group close/reopen custody

Date: 17 November 2026. Scope: the existing Files JavaScript request bridge.
Starting revision: `8b55406621ad7fb728b0409649b38af42dc2926c`.
Original production blob: `c4d40c98f6a6b586692abecfeda71c20b1123666`.

## Coordination and ownership

Jacob appointed the assistant leading the existing 9to1 ongoing task as temporary
lead coordinator until 18 November 2026 at 15:00 UTC and explicitly requested
immediate implementation. The current run's exact three-path claim is recorded
in existing issue #27, comment `6942884418`, under
`cloud-lead-20261117-files-lifecycle`. The private PC ledger was not accessed or
modified. This is a temporary cloud continuation of the same task, not another
coordinator. The final publication/release receipt belongs in that same issue.

Changed paths are limited to:

- `apps/Web/Files/files-browser.js`
- `apps/Web/Files/Tests/files-browser-lifecycle.test.mjs`
- `docs/development/files-browser-lifecycle-2026-11-17.md`

## Reproduced production defect

The original `drain(id)` independently snapshots `group.requests` on each call
and unconditionally deletes the string ID after its snapshot settles. A tracked
request removes itself from that map in its finalizer, before the promise
captured by the first drain has fully propagated settlement. A second drain can
therefore see an empty map and finish first. Once its caller reopens the same
public group ID, the older drain can resume and delete the newly opened group.

The regression invokes the actual exported bridge, starts and cancels an actual
bridge request, overlaps two public cleanup operations, and reuses the group and
request IDs. The observable failure is rejection of a new request with
`Files request group is closed.`, not a private-field or textual assertion.

The new regression suite tests 32 bounded microtask offsets for each of three
close orderings. On the unchanged production source, two tests fail: two group
drains, and a global drain followed by a group drain, each losing the reopened
owner at offsets 6 and 7. The opposite global/group ordering and three additional
lifetime/isolation tests already pass on the baseline. No native Sites defect is
claimed by this reproduction.

## Implementation

Each original group now retains one `drainWork` promise. Every overlapping closer
joins that same original request snapshot, rather than forgetting requests whose
finalizers already removed them from the live map. Retirement also checks that
the registry still points to the exact original group before deleting its ID.

The active-group rejection, cancellation, access-denial handling, stream cleanup,
upload/download APIs and native contracts are unchanged. No replacement bridge,
parallel storage, timing sleep, detached cleanup worker or new dependency was
introduced.

## Executed validation

Environment: Node.js `v22.16.0`; Chromium `144.0.7559.96` via Python Playwright.
The preimage bridge and all three existing JS test files were independently
checked against the exact remote Git blob hashes before using them as evidence.

| Execution | Result |
| --- | --- |
| Existing three JS test files on original production source | 24 passed; 0 failed, cancelled or skipped |
| New lifecycle test file on original production source | 4 passed; 2 failed; 0 cancelled or skipped |
| Production and new test file `node --check` | Passed |
| Full JS suite on the fix | 30 passed; 0 failed, cancelled or skipped |
| Three additional complete fixed-suite executions | 30/30 passed in every execution |
| Chromium runtime: original production module, 96 timing scenarios | 4 observable reopened-owner losses |
| Chromium runtime: fixed production module, identical 96 scenarios | 96 passed; no reopened-owner losses |

Commands, relative to the repository root:

```sh
node --check apps/Web/Files/files-browser.js
node --check apps/Web/Files/Tests/files-browser-lifecycle.test.mjs
node --test apps/Web/Files/Tests/files-browser-lifecycle.test.mjs
node --test apps/Web/Files/Tests/*.test.mjs
```

The 30 tests include the existing three real loopback HTTP cases. New cases also
hold actual ReadableStream cancellation open while multiple closers await it,
verify that the group cannot be reused before that cancellation settles, confirm
its reader lock is released, and check unrelated-group isolation and recovery
from an incorrectly requested active drain.

Chromium evidence executes the exact production module as an ES module with
public bridge calls and disposable responses. It is browser JavaScript-runtime
evidence, **not** hosted UI, authenticated server, native bridge or full-product
acceptance. No browser policy was modified and no external website was used.
Raw TAP logs, the before/after Chromium result and its reproducible harness are
retained with the run's downloadable evidence bundle.

## Publication and boundaries

Publish this reviewed slice on the existing `main`, using the verified starting
head as an expected-ref lease; reject a concurrent head change rather than
force-pushing. Verify published file blob identities against the tested bytes.
The final ending commit SHA and scope release are recorded in issue #27 after
publication; this document does not anticipate a commit SHA that does not exist.

The Web publication and Files/Sites lifecycle workflows are restricted to
`team-b/**`; this run does not create such a branch or dispatch those workflows.
The `docs/development/**` change can trigger existing development-infrastructure
CI. Its previously reported action-pin problem (#39) is outside this three-file
claim and is not silently repaired or treated as a passed CI check here.

The full .NET application, native suites, authenticated hosted create/save/reopen
journey, UI rendering and deployment were not tested. The .NET SDK is absent in
this container. Direct container DNS was unavailable. Connected GitHub and local
Node/Chromium were usable; neither a Sandbox execution plugin nor a subagent
launcher was exposed after discovery. Implementation and review were performed
by the same assistant; this is not independent-subagent signoff.

No PC, GPU/V100, OS setup, production infrastructure, live user storage,
credentials, branch protections or automation schedules were changed.

## Next executable step

Begin the next slice from the actual current remote and this publication receipt,
not from the historical cancellation checkpoint. Obtain a disjoint precise claim
under the current temporary lead mandate. The next product-level gap remains
authenticated browser Sites composition plus a real canonical Files destination
for website creation; source reports in issue #27 require verification and owning
native acceptance before publication. Do not substitute these JS passes for that
work, or rerun them as the only output of another implementation session.
