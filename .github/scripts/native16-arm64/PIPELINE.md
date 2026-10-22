# ARM64 build and acceptance

The canonical specification and current owner decisions govern acceptance. This
recipe produces an internal candidate; it never grants full release acceptance.
The original repository, other worktrees and reviewer evidence remain untouched.

## Order and identities

1. Check exact source, native architecture, pinned SDK, required accessibility
   tools and disk capacity before acquisition or compilation. Run fast Python
   regressions and retain their logs.
2. Restore dependencies by exact version/content pins. Recheck SHA-256 and sizes
   on every inference-cache hit. Browser acquisition and ABI verification still
   authenticate the signed Debian/Ubuntu graphs; a cache never bypasses them.
3. Run a normal restore and full product build for every candidate. NuGet
   download caching and cross-run package reuse are **disabled**: the current
   repository has no reviewed resolved NuGet locks/content hashes. Git revision
   and runner identity alone cannot establish an unchanged resolved graph.
   Do not re-enable reuse until that evidence includes all central/nested
   `Directory.Packages.props`, `Directory.Build.*` and actual resolved packages.
4. Install the exact package after independently checking its source revision,
   architecture, byte count and SHA-256. Retain the candidate and original inputs
   before native acceptance; retention is not permission to reuse an unproved build.
5. Always run real installed-package onboarding/returning profile, normal close,
   local chat/persistence, Chromium rendering/input/HTTP nonce and removal/data
   preservation. Partial uninstall stdout/stderr are streamed to retained logs
   even if the removal command times out.
6. Aggregate original step **outcome**, exact raw statuses, source and harness
   identities, and retained phase failures. `continue-on-error` changes Actions'
   displayed conclusion; it cannot change the checkpoint decision. Missing or
   malformed evidence is an infrastructure blocker; ambiguous runtime failures
   remain unclassified until triaged rather than being guessed as product bugs.
7. Independently review screenshots and the full mandatory release suite on the
   intended platform. Ubuntu24.04 ARM/Xvfb is not Ubuntu26/GNOME laptop acceptance.
   Package production, focused native checkpoint and release readiness stay
   separate. Only an accepted package may be delivered through the supported
   authenticated laptop path with path, bytes and SHA receipt.

## Fast checks

Run from the repository root, using Python with PyYAML installed:

```sh
python3 -m unittest discover -s .github/scripts/native16-arm64 -p 'test*.py' -v
python3 -m unittest discover -s .github/scripts/native16-arm64 -p '*_test.py' -v
python3 -m unittest discover -s .github/scripts/native16-arm64/chromium-native -p 'test*.py' -v
```

These are infrastructure/component tests, not native product acceptance. The
unmodified captured AT-SPI fixture identifies the canonical Home catalogue;
negative cases still reject absent, hidden, disabled or zero-size required
controls. Durable onboarding completion and model-eligibility checks remain in
the actual native driver.

## Maintenance and failure recovery

- Update exact action commits, SDK, upstream/model/source pins and corresponding
  licences through review. Refresh changed hashes in `inference-source-manifest.json`
  and `native16-source-identity.json`; never relabel an old result for new inputs.
- Cache misses reacquire through authenticated/pinned origins. An expired origin
  with no verified cache is BLOCKED. Do not substitute an unreviewed engine,
  quantisation, ABI shim or disabled sandbox. Rebuild the existing pinned supplier
  through its established workflow, independently verify it, then review the new
  provider identity before changing acquisition pins.
- Retain original failures, source, package identities and licences for14days in
  authenticated Actions artifacts. Caches are an optimisation, not durable custody.
  `retain-immutable-artifacts.py` is a fixed historical rescue operation for failed
  run39698883180, not a generic deployment tool. Its successful retention run is
  39823401298; original archive identities remain intact inside artifact12444983091.
- GitHub dispatches require an already registered workflow. A new filename on an
  isolated branch may return404. The historical rescue reused the registered
  workflow on its isolated retention branch only; it did not edit the default
  branch or run packaging. Preserve that404 as an infrastructure failure.

## Current blockers at this change

| Blocker | Owner | Evidence / next action |
|---|---|---|
| ARM immediate-window normal close timed out | Profile/native lifecycle worker | Failed run39698883180; original startup-final-clean exception. Fix product and rerun real native checks. |
| NuGet/package cache lacks reviewed resolved lock inputs | ARM packaging | Reuse disabled; normal restore/build continues. Introduce reviewed content-hash locks before enabling reuse. |
| Pinned Bookworm provider expired before initial cache seed | ARM packaging | Artifact12328502225; preserve immutable inputs and verify a refreshed supplier before dispatching another full candidate. |
| Owner Ubuntu26/GNOME laptop acceptance and delivery | Release coordinator | NOT RUN; first require mandatory native gates, then supported authenticated delivery receipt. |

The obsolete `Open Chat` Home predicate and YAML1.1 parsing of Actions' `on` key
are repaired by regression-tested infrastructure changes. That repairs the test
contract; it does not retroactively change the failed native run to a pass.
