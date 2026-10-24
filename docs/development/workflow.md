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

   After an execution-environment restart, a resumed worker can retain an unusable
   environment binding while the coordinator's actual shell works. A running
   environment status or saved worker label is not a shell-access test. Preserve
   the affected worker's source, dirty diff, file modes and hashes, and stop its
   source writes through the supported collaboration interface. If that interface
   supports a fresh isolated worker, launch it with a fresh context and the exact
   existing instructions, checkpoint, worktree and scope. Require an actual `pwd`
   and source-read probe before transferring the old claim with `release`/`claim`
   at the current generation. Preserve the original worktree and checkpoints;
   never create a replacement project, guess a host, or bypass a worker limit.
   If no supported fresh binding works, record BLOCKED and continue independent
   work. A fresh context also needs the normal recovery procedure; access alone
   does not establish product acceptance.

   A new Git worktree created with `--no-checkout` can have an empty index. Finish
   its initial checkout through Git and verify HEAD, sparse selection and clean
   status before allowing writes. Do not mistake that initial empty index for
   intentional product deletions or carry it into an integration commit. Retain
   any failed initialization diagnostics before repairing the new worktree.
   `read-tree` can populate the index without materialising selected sparse files.
   In a new owned checkout, finish with `git checkout-index --all`; verify selected
   files on disk and clean status. Never apply this as a reset of active work.

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

## Execution custody after restart

Retained evidence and an intact original repository do not establish that the
active candidate or its build graph still exists. `recover` now also probes every
legacy `development_candidates` record that declares a `worktree`: the actual
root, exact HEAD and reachable objects must still be available. A missing `/tmp`
candidate blocks recovery even if the projection's original HEAD is unchanged.
Revision/status-only historical records remain supported but report UNRECORDED
execution custody. CHECKPOINT means recovered engineering state, never execution
or native readiness. Historical PASS/READY labels do not change these checks.

The coordinator can publish `execution_custody.NAME` with its same-named
`development_candidates.NAME` in one CAS checkpoint. A custody record contains:

- `revision`: the candidate's full 40-character Git commit ID; `owner`: actual
  responsible owner; `source`: one of the records below.
- `build_dependencies`: named prerequisites, each with absolute `path`, `identity`
  (`kind=file`, `bytes`, `mode`, `sha256`), `recovery` (`rebuild` or `restore`) and
  concrete `next_action`. Use `identity: null` only for an actually missing,
  unmaterialised prerequisite. This records a blocker; it cannot become ready
  merely because a file later appears. Capture the actual regenerated identity
  in a new CAS update. No durable build cache is required.

A `source` always has `kind`, absolute `path`, and `git` custody containing
absolute `path` (the actual Git worktree or object repository), absolute
`common_directory`, `revision` (source/base commit), and `required_revisions`
(list of additional full commit IDs). Candidate, source/base, declared prerequisite
commits and any candidate `base_revision` must have actual reachable object
closures. A worktree source (`kind: git-worktree`) additionally has a nonempty
`files` map of repository-relative paths to exact regular-file identities. Its
Git path is its own root, and its actual HEAD must equal the candidate revision.
This supports sparse, declared source scopes without pretending that they are
complete application graphs. Explicitly declare every input needed for the next
owning command; undeclared inputs are outside this check.

An export source (`kind: source-export`) instead has `manifest`: an exact retained
regular-file reference (`path`, `kind`, `bytes`, `mode`, `sha256`). No Git HEAD is
invented inside the export. Supported manifest schema is
`9to1.exact-source-export/v1`, with absolute `root`, `origin_git`,
`source_revision`, and nonempty `files` list (`path` relative to root, `bytes`,
`mode`, `sha256`). Duplicate/escaping paths refuse admission. Root/origin/base
must match custody; every listed regular file is rehashed. The manifest itself
is parsed from the same bounded no-follow read whose bytes are hashed. A
composed variant may explicitly supply `candidate_revision` distinct from its
source/base revision; both Git commits still need their object closures.
Retain the original manifest and review/capture a new complete variant manifest;
there are no implicit source overrides or inherited acceptance claims.

New active path/custody publications validate actual source and all captured
prerequisite identities **before mutation**. Malformed/stale claims refuse the
checkpoint without advancing generation. Updating a candidate with existing
custody revalidates the resulting pair. Task-only checkpoints do not rehash old
execution inputs, so losses can still be recorded without rewriting history;
`recover` and state-aware `preflight` expose those losses.

Use the projection's original repository for ordinary recovery. For a scoped
candidate or an export, select its explicit name:

```sh
python3 -B eng/development/workflow.py --state "$NINETOONE_DEVELOPMENT_STATE" recover --repo /actual/candidate-or-export --execution NAME
python3 -B eng/development/workflow.py --state "$NINETOONE_DEVELOPMENT_STATE" preflight --repo /actual/candidate-or-export --execution NAME
```

Named recovery still reconciles `repository`/`observed_revision` against the
original projection; it separately reports requested context and selected source.
It never rewrites either identity. Without an explicit name, using a component
repository instead of the original remains a discrepancy. Unknown/unrelated named
contexts refuse. Malformed named source/Git/path shapes and non-object export
JSON also return structured INFRASTRUCTURE/BLOCKED responses, with no checkpoint
mutation or generation advance. Export manifest size must match its receipt before
the bounded read. Content stability checks device/inode/size/mtime/ctime, excluding
access time changed by the read itself. Default recovery checks all declared candidates; named recovery
checks the selected one and retains the full owners/tasks/gates/next actions.
`execution_next_actions` adds restore/rebuild actions without changing state.

`DECLARED_INPUTS_AVAILABLE` is limited to checked source and nonempty captured
build prerequisites. Omitted build prerequisites remain UNRECORDED. Missing build
inputs are INFRASTRUCTURE prerequisites with rebuild/restore actions, separately
from source loss. This does not run compilation, establish a live worker, verify
a toolchain/platform or satisfy native/release acceptance. Preflight without state
retains its original generic source-only scope; with state it also requires
recorded execution inputs. No helper restores files, dispatches work, schedules
hardware or creates another coordinator.

## Verification and reuse

Record verification scope from the actual `UnitTestResult` names and counters.
`workflow.trx` rejects empty, inconsistent or skipped runs; retain the result names
as well and check that every required method and parameterised case ran. Check
labels, filenames and guessed method counts do not establish app coverage. A
passing Files dependency check cannot stand in for native Build acceptance.

A RED proof must reach its intended regression after valid fixture setup. Retain
a purpose-specific diagnostic or assertion sentinel and its actual stack. Compiler,
fixture, dependency or resource failures are not a demonstrated RED. Preserve them,
correct the concrete cause and retry only affected stages. Do not rerun passing
checks merely to fix reporting or count bookkeeping.

Preserve the primary body failure and independent cleanup failures with their
original cause references; cleanup must not hide the original defect. New platform
fixtures must report a genuine skip/NOT RUN outside their supported platform,
never pass through an early return. Mandatory native and release gates remain
required. Check the current resource guard before launching; an exhausted host is
BLOCKED, and an empty log or process exit zero cannot substitute for test results.

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

Source capture caches are immutable artifacts, separate from mutable SDK graphs.
A successor may share unchanged files only with a fully verified immutable source
capture; never link worker or SDK sources into it. Materialise each changed file
through a new temporary inode and atomic replacement. Do not write or chmod a
shared inode. Verify the complete new path/mode/hash set, the old capture unchanged,
and no aliases to mutable sources. Retain delta and custody receipts. For Git tar
exports, set `git -c tar.umask=0022 archive` so original executable modes survive.
Use Python `-B` for source validation to avoid adding bytecode to exact captures.
Source-cache reuse proves neither build success nor native/release acceptance.

Before selecting a test filter, confirm the owning project's actual target and
Compile inputs. A source file beside a project may be excluded from that target;
use its existing owning project rather than expanding an unrelated suite. Reject
zero executed cases and compile/analyser errors as infrastructure failures, never
as a passing result or evidence that a defect was reproduced. Preserve the failed
command and outputs when correcting selection; keep valid assertions unchanged.

## Integration and immutable release candidates

`main` is the owner's canonical completed-work integration branch. Parallel unfinished work stays on its existing isolated branch; after source review, applicable owning checks and independent review, Root integrates the exact completed change promptly through this existing workflow. Compare its base, dependencies and retained accepted-fix identities before merging or replaying a curated change. A branch label or an ancestor relationship is not a readiness result.

Root initialises or advances local `main` from the identified reviewed integration checkpoint with an expected-old-ref check. An absent local `main` must be created without checking out, resetting or rebasing an active or dirty worktree. Keep source-only staging separate, preserve historical refs and unfinished work, and record the integrated source/evidence and still-open gates in the existing CAS projection. Use the existing review/PR mechanism within the current publication permission; do not create another ledger or integration workflow.

Synchronise `main` under the existing repository access, publication rules and owner instructions; this policy adds no new approval requirement. Keep credentials and private evidence outside Git. `main` does not satisfy RC pinning, native/package/platform acceptance or the seven quality criteria; the immutable release process below remains mandatory.

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

For owner UI feedback, collect a small set of screenshots as part of an existing required UI/native run, without a separate rebuild merely to obtain images. Capture changed screens or meaningful checkpoints and include them in the existing progress report. Each capture records the exact source/build, time, screen, theme, viewport, platform and whether it is native or headless. Reuse only demonstrably applicable unchanged outputs; distinguish older images from the current candidate. Retain capture failures without concealing product failures or creating substitute mockups. A screenshot is feedback evidence, not acceptance by itself.

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

## Owning copy budgets before SDK launch

Run the read-only budget check before an owning SDK step when a reviewed copy
manifest is available:

```sh
python3 -B eng/development/workflow.py copy-budget --manifest /actual/frozen-copy-manifest.json --sha256 REVIEWED_SHA256
```

The manifest pins the package origins, complete destination contents and expected
source identities. The checker rehashes real inputs and outputs, refuses stale
identities and symlinks, and budgets missing or stale complete copies separately
for each actual filesystem, preserving the 256 MiB reserve. Possible hardlinks
receive no optimistic credit. Existing verified identical outputs may reuse
their bytes. Inputs are read only: no SDK, model, copy, cleanup or ledger write.

Exit 2 refuses the listed copy budget or its inputs. Exit 0 means only that the
listed conservative copy budget fits; `sdk_launch_admitted` remains false. It
never establishes a complete conditional/custom-target copy closure, scratch
peak, toolchain/native environment or launch permission. Preserve the owning
resource, memory, timeout and process-custody guards and obtain those remaining
requirements separately. The Native17 disk-full failure demonstrated this gap:
its 468 missing verified copies alone require 275,447,808 bytes in addition to
the 268,435,456-byte reserve. Retain the original failure and do not retry when
only the reserve has been recovered. The existing workflow test entrypoint also
loads the copy-budget regressions; broad discovery runs each suite once.

## Temporary owner priority — implementation until 03:00 BST, 24 October 2026

Read [the latest implementation-window decision](../specification/owner-references/2026-10-23-implementation-window/owner-correction.json). Until **24 October 2026 03:00 Europe/London (02:00 UTC)**, prioritise implementation of feature gaps and basic smoke checks. Defer full acceptance and acceptance-only audits until that time; preserve their queue and evidence. Then resume independent acceptance from the exact latest candidate, unless a newer owner instruction changes the priority. This defers gates; it does not waive them or authorise an unaccepted release. Keep ordinary development, scoped Thunder work and progress reporting moving.

## Current execution admission immediately before launch

`execution-admission` is a read-only consistency check over the current existing ledger, not another admission record. It requires the named source-export candidate and exact manifest pin, `build_dependencies.actual-guarded-sdk-runner`, and the adopted `recovery_assets.latest-root-checkpoint` / `last_root_checkpoint_observed` pair. That pinned ROOT-LIVE must bind the same candidate, source manifest and `sdk_admission.runner`; its expected CAS generation must already have been reached. The actual manifest, Root proof and runner must still match their regular-file path/bytes/mode/SHA pins. Missing or stale historical admission refuses launch without changing legacy recovery behavior.

Prefer calling `execution_admission(state_path, execution, source_path, revision, manifest_path, manifest_sha256, runner_path)` inside the admitted Python runner immediately before `subprocess.Popen`, with `runner_path=Path(__file__).resolve()`. A refusal raises and must stop that launch. The CLI form must also control the following command:

```sh
python3 -B eng/development/workflow.py --state "$NINETOONE_DEVELOPMENT_STATE" execution-admission \
  --execution cloud-owning-v2 --source "$NINETOONE_EXECUTION_SOURCE" \
  --revision "$NINETOONE_EXPECTED_REVISION" --manifest "$NINETOONE_SOURCE_MANIFEST" \
  --manifest-sha256 "$NINETOONE_EXPECTED_MANIFEST_SHA256" --runner "$NINETOONE_GUARDED_RUNNER" &&
python3 -B "$NINETOONE_GUARDED_RUNNER" "$NINETOONE_ADMITTED_STAGE"
```

Use the expected values captured for the intended command, not a stale shell snapshot or an automatically substituted newer runner. Exit2 is INFRASTRUCTURE/BLOCKED; retain the failed CAS/admission receipt and reconcile through the existing coordinator. Do not follow refusal with an unconditional launch or mint a new approval record.

The helper re-reads relevant current ledger pins before returning and checks only the pinned manifest metadata, not every exported source file. The admitted owning runner retains its full source check and resource/memory/timeout/process-group guards. No SDK process, ledger write, ownership claim, allocation, acceptance or RC permission is created. This check does not hold a lease over later caller actions; invoke it immediately before launch under the existing exclusive coordinator custody.

## Reproducible native candidate input identity

Use `python3 -B eng/development/native_source_identity.py --repo /actual/existing/repository --revision EXACT_COMMIT --out /fresh/private/identity.json` against an actual immutable committed input. It reads committed objects, preserves the original minimum base and prior pins, includes changed regular inputs, records gitlinks/deletions, and excludes `.github` from the product diff to avoid circular identity. Review and compose the resulting metadata through the existing Root workflow; never replace it from a dirty checkout or treat source hashes as build/native acceptance. The output path must be fresh and outside Git/worktrees. Run `python3 -B -m unittest discover -s eng/development -p test_native_source_identity.py` for its real tiny-Git controls. Historical acceptance stays provenance; the new candidate must retain NOT RUN until its own mandatory gates pass.

After a Root composition has successfully created its commit/ref and persisted `COMPOSITION.json`, prepare its explicitly closed alternative index with `eng/development/closed_root_index.py --closed` and the exact repository, index SHA, committed revision/tree, fresh archive and receipt paths. The tool is bounded and prepare-only; it never unlinks, grants execution or updates the ledger. Root must adopt the verified archive/receipt at the current generation, recheck exact closed-index custody, and only then explicitly reclaim that raw cache. Keep the full binary index recoverable, including stat/cache bytes; a reconstructed tree is not a byte-identical backup. Never run cleanup in error/finally paths or target a repository/worktree index. Source/native acceptance remains separate.

## Python maintenance tools

Generate native-input metadata from a real immutable commit, never a dirty checkout or export:

```sh
python3 -B eng/development/native_source_identity.py --repo /actual/existing/repository --revision EXACT_COMMIT --out /fresh/private/identity.json
```

The generator preserves the original minimum `source_commit`, all prior pins and historical acceptance, includes changed regular inputs and records gitlinks/deletions. Missing/nonregular prior pins or a base that is not an ancestor refuse generation. The product diff excludes `.github` to avoid circular metadata hashes. Root reviews and composes the output through the existing workflow after the real input commit exists. Keep output fresh and outside repositories/worktrees; never narrow the pin list to bypass missing inputs. Regenerated metadata grants no current native/package/release acceptance.

Only after a successful Root composition commit/ref and persisted `COMPOSITION.json`, prepare an explicitly closed alternative-index backup:

```sh
python3 -B eng/development/closed_root_index.py --closed \
  --repo /actual/existing/repository --index /private/9to1-root-composeNN.index \
  --expected-sha256 EXACT_INDEX_SHA256 --revision EXACT_COMMIT --tree EXACT_TREE \
  --archive /fresh/private/closed.index.gz --receipt /fresh/private/closed-index.json
```

The bounded prepare-only tool preserves the raw index, refs, worktrees and ledger. Root separately reviews/adopts the exact archive and receipt, rechecks closed-index custody, then explicitly reclaims the duplicate. Never target an active/repository/worktree index or put cleanup in error/finally paths. `read-tree` cannot reproduce the original binary stat/cache bytes.

The llama.cpp benchmark requires a completed choice and terminal `[DONE]`; incomplete/error streams refuse a successful measurement. `requestCompletionTokensPerSecond` measures the whole request interval. The legacy `completionTokensPerSecond` keeps its labelled post-first-visible-content interval; neither is native decode speed. Missing usage remains null.

Run the focused suites against the intended candidate and its existing `workflow.py`:

```sh
python3 -B -m unittest discover -s eng/development -p test_native_source_identity.py -v
python3 -B -m unittest discover -s eng/development -p test_closed_root_index.py -v
python3 -B -m unittest discover -s '9to1 Workspace/Home/Source/Dulche/llamacpp/tests' -p test_benchmark.py -v
```

These disposable Git-fixture and mocked stream controls do not contact a model/socket or establish SDK, hardware, native, app or release acceptance. Keep existing runtime/model admission, ownership and quality gates.

The existing development-infrastructure CI workflow registers these three suites, requires their current 17/36/22 tests with zero skips, and retains logs, receipts and exact inputs even on failure. Its pull-request and push path filters include the benchmark and its test module. This registration is not evidence that a particular CI run passed.

Historical evidence assets must point to immutable revision-bound snapshots, never a mutable source-export leaf. Before the next composition changes a pinned historical file, retain its exact committed bytes at an exclusive private recovery path, record its revision/bytes/SHA/mode, and atomically update the existing ledger asset/relocation. Keep the current source separate; changing a current file must not invalidate earlier evidence.

For a source-export publication, the authoritative `development_candidates.NAME.manifest` must exactly equal `execution_custody.NAME.source.manifest`; the mistaken `source_manifest` shadow is refused. If a candidate declares `tree`, it must equal its actual committed Git tree. These checks run before a publication mutates state; historical read-only records retain their prior semantics. Use the current reviewed candidate's `workflow.py` entrypoint for coordination writes after an upgrade, rather than importing an older worktree helper that lacks the new publication checks. Preserve the original worktree and its uncommitted changes.

## Current implementation-first execution mode

The [24 October direct owner override](../specification/owner-references/2026-10-24-implementation-first-until-complete/owner-correction.json) pauses full independent acceptance until all canonical apps are100% implemented or the owner explicitly resumes it. This replaces the earlier timed03:00BST resumption. Prioritise implemented features and fixes, actual compilation and focused basic smoke tests. WIP may enter the development candidate without waiting for acceptance. Keep Accepted estimates frozen and preserve unfinished checks/failures; a build, screenshot or implementation estimate does not grant release acceptance. All specified scope and mandatory native, installation, persistence, shutdown and delivery gates still apply before any future accepted/released claim. Existing historical acceptance evidence is retained.

## Fresh implementation checks during the acceptance pause

The current owner pauses full acceptance until all canonical apps are fully implemented or explicit resumption. Run affected fresh normal compilation and focused genuine smoke checks promptly. The complete reviewed toolchain/dependency closure in the `reusable()` section governs evidence reuse; it must not become an acceptance-only gate in front of every fresh Layer1 compile. Keep source/custody/current SDK host and exact command, normal dependency/copy and private writer checks, and the existing memory/disk/time/process safeguards. A fresh result with partial dependency coverage is explicitly PARTIAL and NOT REUSABLE. It grants no product/native/release acceptance and cannot stand in for mandatory future checks. Keep parallel implementation moving while the one physical SDK source is briefly frozen for its actual command.

## Visible local teams

The [OpenCode team adjunct](opencode-teams.md) uses real team-leader session tabs,
Luna child workers and one Astra supervisor in the existing coordinator ledger.
Its five-minute leader and thirty-minute supervisor ticks coalesce when busy.
Astra's five-minute budget is cooperative because the inspected native API has
no hard per-prompt deadline; do not describe it as enforced. Reconcile uncertain
creation or prompt receipts before retrying, and revalidate source, scope, model,
provider and capacity immediately before dispatch. A configured team is not a
running worker. Follow the [portable handoff](local-handoff.md) and exact source
receipt; never pull blindly over an active checkout or interrupt Thunder services.
See the team procedure for commands, native capability limits and actual launch
gates. This extends the existing CAS projection and coordinator, with no second
programme supervisor or replacement milestone database.
