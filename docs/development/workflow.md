# Development, integration and recovery

This is the operational companion to `AGENTS.md`. Product authority remains the
[canonical specification](../specification/9to1-development-specification-v2.md)
(SHA256 `3724566ec5f564164b55bab6c7370f780766bafbb2279dee6b1835fb66806f96`),
APP-AC-001–009, GOV-001–002 and later explicit owner decisions. Read the
[22 October owner amendment](../specification/owner-references/2026-10-22-development-reliability/owner-correction.json).
Mercury is revoked. Normal development uses actual supported Beam/Codex execution;
historical directories containing “mercury” remain useful source/evidence. At an
actually observed **10% remaining Codex allowance**, the owner's conditional
exception permits handoff to a genuinely verified Thunder development worker.
If that worker is unavailable, preserve the handoff, commit and pause. The last
owner reading was 99%; forecasts, context tokens and other provider limits do
not trigger this rule. This current amendment governs older receipt summaries.

The registered coordinator is the owner's **laptop**, not the RTX desktop.
Inference access does not provide deployment or administration access. Preserve
existing work and reconcile actual shared model custody before requesting
another Thunder worker. A bridge's saved monitor/session-active flag alone does
not establish a current model consumer. The current owner amendment records the
desktop task channel's verified scope and its remaining unverified capabilities;
read it before setup or inference, rather than restoring older endpoint assumptions.

## Resume and ownership

1. Read `AGENTS.md`, this document, applicable specification sections and the
   latest owner decisions. Read the **latest actual** existing ROOT-LIVE cloud
   checkpoint and PC coordinator status; neither an old label nor an old PID
   establishes current code, live workers or endpoint execution capability.
2. Inspect actual repository/worktree custody, HEAD, uncommitted changes,
   supported live workers and current CI **outcomes**. Preserve work and failures.
   A Git worktree can exist while its `/tmp` alternate object store is gone.
   `preflight` checks reachable HEAD objects and alternate paths; retain dirty
   source separately. Never initialise a replacement project to hide missing work.
3. Recover the existing cloud checkpoint projection:

   ```sh
   python3 -B eng/development/workflow.py --state "$NINETOONE_DEVELOPMENT_STATE" recover --repo "$NINETOONE_REPOSITORY"
   ```

   The state path is private machine configuration outside Git, recorded in the
   latest ROOT-LIVE file. Missing state/assets or source discrepancies are
   BLOCKED, not a reason to overwrite a stale ledger. Recovery does not prove
   native tests or worker liveness. There is no configured runtime compaction
   callback; this mandatory startup/handoff procedure is the supported fallback.
   `projection_of` remains the original projection's provenance. When the
   coordinator records `last_root_checkpoint_observed`, also retain its exact
   path and file identity as `recovery_assets.latest-root-checkpoint` in the same
   CAS checkpoint. Recovery selects that pinned observation for `read_first`,
   never a filename/timestamp guess; it reports the original separately as
   `projection_origin`. Missing, changed, mismatched or unpaired newest inputs
   block recovery without falling back to older instructions. Legacy states
   without either newer field use their retained original checkpoint.
4. Reuse current owners where possible. Claim exact paths or a trailing `/**`
   subtree before work. Keep product, framework/native, Thunder, CI/packaging,
   independent QA and release integration/delivery ownership distinct. New
   agents require supported launch APIs and isolated worktrees. One heavy
   SDK/native task uses the existing coordinator allocation at a time. Other
   workers continue source work and lightweight tests.

   OpenCode v2 can select the inherited `PWD` even when the process `cwd` was
   changed. Set both to the assigned worktree and confirm the actual supported
   server/session boot location before allowing writes. A correct PID is not
   sufficient. The check runner synchronises `PWD`, records its new-session
   PID/PGID/start ticks, bounds awaits and signals only a still-proven owned group.
   Remaining live group members block success. A SIGKILL/container/exec-server
   restart can still orphan children: inspect actual process-group/session custody
   after interruption rather than claiming guaranteed cleanup or reusing old PIDs.

`eng/development/workflow.py` extends the existing ROOT-LIVE checkpoint system.
It is an evidence/ownership **projection**, not a new supervisor or replacement
for the PC milestone ledger. It cannot dispatch remote commands. Existing
read-only bridge operations remain read-only; a delivery journal ID is not proof
that a worker executed a task. Publication/authentication/delivery use only the
existing supported mechanisms.

## Atomic checkpoints and blocker dashboard

The coordinator creates the projection once from a real existing checkpoint:

```sh
python3 -B eng/development/workflow.py --state "$NINETOONE_DEVELOPMENT_STATE" init --repo "$NINETOONE_REPOSITORY" --root-checkpoint /actual/path/ROOT-LIVE-latest.json
python3 -B eng/development/workflow.py --state "$NINETOONE_DEVELOPMENT_STATE" claim --expect 0 --worker actual-session-id --role implementation 'exact/owned/subtree/**'
```

Updates take a filesystem lock, require the expected generation, preserve
unrelated fields and publish with fsync + atomic rename. `checkpoint --expect N
--patch patch.json` merges named records; stale writers must reread and reconcile.
Protected RC/worker fields require their dedicated commands. Never copy a stale
local projection over the authoritative coordinator ledger.

Checkpoint patches that touch recovery records are validated before publication.
Each incoming `recovery_assets` entry requires exactly `path`, `kind=file`,
`bytes`, `mode` and lowercase `sha256`; the absolute path must identify actual
matching regular-file bytes. A new `last_root_checkpoint_observed` is a record
with absolute `path` and `sha256`, never a path string. It must match the resulting
`latest-root-checkpoint` asset in the same candidate, so changing the current
pair requires one complete CAS update. Invalid, missing, changed or nonregular
inputs refuse the update without changing state or advancing its generation.
Legacy projections without a current pair remain supported. Task-only updates
do not rehash recovery files, and adding an unrelated valid asset does not
revalidate historical files; `recover` still reports any missing retained inputs
until their coordinator-owned repair is recorded.

Tasks record ID (map key), owner, components, stage, status, dependencies, last
verified revision, evidence, integration status, release readiness, last meaningful
progress and next executable action. Use COMPLETED, VERIFIED, CHECKPOINT,
WORKING, STALLED or BLOCKED. Record unexecuted checks explicitly as NOT RUN in
evidence; do not invent a passing task. `dashboard` renders the actual structured
state. `verified_revision` defaults to null; observed HEAD is a different field.
VERIFIED records require matching task/evidence source revisions, retained regular
evidence files, actual steps and a different reviewer/implementer. An old PASS
label or missing artifact cannot promote the current revision.

Before interruption/compaction: safely finish or checkpoint the current command;
record exact revisions/candidates, dirty/untracked work, source packs, raw failures,
scope ownership, pending integration/acceptance, blockers and ordered next actions.
Keep source packs, manifests and logs in durable custody or authenticated retained
CI artifacts; `/tmp` is scratch. `recovery_assets` contains exact path/bytes/SHA256/
mode records; recovery rehashes them and refuses missing or changed assets.
Keep credentials outside Git and outside recovery packs.

The committed Git bundles are incremental and require their recorded base
revision; a source ZIP is not a whole-repository backup. Recovery targets the
existing durable repository and must reject missing base objects or payloads.
Early schema-1 snapshots made before the `worktree_clean` field was introduced
cannot be promoted by this helper. Regenerate them from complete actual source
with `snapshot`, review the new identity and rerun applicable acceptance; never
add the field to an old signed-off manifest or reuse its acceptance implicitly.

## Verification and reuse

| Layer | Scope | Promotion limit |
|---|---|---|
| 1 | Static/format/configuration, affected compilation, owning unit/component checks | Development evidence only |
| 2 | Cross-component interfaces, persistence/recovery and relevant regressions | Reviewed integration evidence |
| 3 | Actual native startup/close, UI, browser input/rendering, local chat, saving/reopening, screenshots | Specific platform/journey evidence |
| 4 | Full mandatory regression, actual installer/platform, packaged end-to-end journeys, independent specification/visual review, delivery validation | Release acceptance only |

`plan --changed changed-paths.json` uses `eng/development/checks.json` to select
the existing root build sets and their dependencies. Unmapped changes select the
whole registered set and require the owning manifest checks; the registry is not
an exhaustive app acceptance manifest. `--release` never removes mandatory gates.
Extend selectors when an actual dependency is established. Do not repeat a broad
suite after focused checks pass unless changed inputs or unresolved concerns justify it.

`snapshot --repo REPO --out IMMUTABLE.json` binds complete tracked and ordinary
untracked source contents, revision, modes, symlinks and initialized pinned donor
sources recursively; sparse/incomplete inputs and unresolved index conflicts fail
closed. Output belongs outside the repository. `--accepted-fix FIX.json` includes
independently reviewed file identities and evidence. `--previous-candidate OLD.json`
requires all prior accepted fixes; changed fixes require an explicitly reviewed
predecessor hash. A retained ID alone cannot hide overwritten source.

`run --repo REPO --candidate SNAPSHOT --check ID --layer 1 --out FRESH-DIR
--trx FRESH.trx -- COMMAND...` retains exact command, platform, duration, exit code,
raw log and actual nonzero TRX counts. Tests exiting zero without execution evidence
remain UNVERIFIED. Existing TRX files are refused. Changed inputs invalidate the
run. A zero build exit is PROCESS_SUCCEEDED, not product or release acceptance.
Use owning native/package harnesses for real application checks; never replace
them with fabricated TRX/JSON. The runner is a trusted workflow helper, not a
sandbox against arbitrary supplied commands.

Python callers may use `reusable(receipt, candidate, command, platform)` only with
a reviewed toolchain/dependency closure (`--environment-manifest`: reviewer plus
absolute file identities). It rechecks exact source identity, command, platform,
runtime/dependency bytes and retained log/test results. Without that closure reuse
is refused. This deliberately conservative first implementation reuses the same
immutable candidate, rather than assuming that a different HEAD is unaffected.
Platform-specific build caches remain checksum/version keyed in their owning
pipelines. A cache hit never substitutes for mandatory release acceptance.

## Integration and immutable release candidates

Implementers retain focused evidence and hand off exact source/dependency identity.
An independent reviewer validates the affected requirements before integration.
Resolve file/scope conflicts first; compare retained accepted-fix identities so a
merge cannot silently remove a verified fix. Integrate completed compatible work
promptly without rebuilding identical inputs in each worker.

`pin-rc --expect N --candidate SNAPSHOT` pins an immutable candidate and manifest
identity separately from active development. Existing RCs cannot be overwritten.
Dirty snapshots remain development checkpoints and cannot be pinned as releases.
For a necessary release fix, create a new snapshot and `advance-rc` with a reviewed
patch record: ID, reason, different reviewer/implementer, exact source/target
candidate IDs, actual changed file list and active-development backport targets.
Unrelated undeclared source changes are rejected. The predecessor remains in
`release_history`, old-candidate evidence cannot satisfy successor acceptance,
and backports remain explicit pending changes. `verify-backport` checks exact
reviewed file content in each target, recording actual HEAD. Release stays blocked
until required backports are verified and their retained target files still match.
Generic checkpoint updates cannot overwrite controlled RC-backport records.
Review semantic compatibility and normal
branch cleanliness separately before merging; matching file bytes alone is not
independent application acceptance.

`release-status` requires the exact pinned candidate, retained real evidence,
independent reviewer, platform and steps for every mandatory gate; the independent
acceptance gate also records all seven quality criteria. Missing/tampered evidence,
missing native workflows and green build steps cannot make a release ready. The
tool validates applicability/structure, not the semantic truth of reviewer claims;
independent reviewers must actually execute and inspect the specified workflows.
Separate packaging, native runtime, complete acceptance and authenticated delivery.

## Troubleshooting and evidence

Classify failures from actual evidence as INFRASTRUCTURE, TEST or PRODUCT; leave
ambiguous failures UNTRIAGED. Missing SDK/dependency/object store/endpoint is an
infrastructure blocker. A wrong test signature requires a regression proving the
repair still rejects the intended product defect. Actual app crashes remain
product failures. Preserve original failed attempts and exact outputs.

Before a normal test: check production DI/service/image-loader registrations,
actual owning project references, restored assets/toolchain identity, deterministic
fixtures and available native/display/browser/model dependencies. Use production
project targets as declared: globally forcing a portable `TargetFramework` during
restore can break donor analyzers requiring `netstandard2.0` (NETSDK1005). A Linux
restore of legitimate Windows multi-target projects may require
`EnableWindowsTargeting`; prove each owning project's normal assets/required TFM
instead of treating a top-level restore exit as graph readiness. Do not remove
analyzers, references or supported targets to silence NETSDK1100/1005.
Use production
composition behind the existing typed infrastructure boundaries; no alternative
app authoring or mock native success. CUI/theme changes require representative
downstream UI checks. Icons need real asset/loader validation; names or manifests
alone do not prove rendered icons. ARM dependency acquisition must retain its
version/checksum/licence provenance; do not weaken ABI/sandbox/authentication gates.

GitHub step `conclusion: success` can mask `outcome: failure` under continue-on-error.
Inspect the raw owning receipt and final aggregator. Keep actual failure artifacts;
do not relabel the combined application from isolated passing components.

Record actual build/test durations, classified failure counts, integration and
acceptance wait timestamps and RC failures where available. Do not invent savings.
Half-hourly owner emails remain brief: completed changes, current work, next work,
screenshots with honest freshness, Thunder results/limitations, infrastructure
blockers, release readiness and demonstrated recovery. Send only through the
already authorised reporting path; never expose keys, private files or packages.

## Focused infrastructure validation

```sh
python3 -B -m unittest discover -s eng/development -p test_workflow.py -v
```

The suite uses real disposable Git repositories, filesystem changes and concurrent
writers. Its evidence-validation fixtures are **not** product/native acceptance.
The infrastructure CI workflow retains exact inputs and the actual job result.
A separate fresh-context recovery review must restore durable source, read the
instructions/state, recover owners/gates/next actions and reject missing artifacts.
The independent 22 October review of `90d5803b` passed all 56 owning invariants,
the four reported rejection/positive-control pairs, actual existing-repository
recovery and missing/corrupt required-artifact rejection. Its retained receipt is
located by the current ROOT-LIVE checkpoint under
`development-workflow-overhaul-20261022/independent-recovery/successor-90d5803b/VERDICT-90d5803b.json`.
That result covers the reviewed helper revision and that recovery scope; product,
native, ARM release and later candidate acceptance remain separate requirements.
