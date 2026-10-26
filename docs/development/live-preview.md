# One local development copy

`eng/development/live_preview.py` launches the normal source-built Haven apphost
without installing a new `.deb` for each fix. The development launcher stays the
same; each approved build occupies its own immutable runtime directory. All runs
use the same explicitly chosen `HAVEN_DATA_DIR`, preserving the actual profile,
documents, browser state and logs. It never migrates or deletes application data.

This is a local process helper for the existing development workflow. It does not
build, invoke models, start a server, schedule work, replace the coordinator, or
establish a PC connection. The currently observed bridge cannot activate this
copy: the owner or an already-authorized local worker must run the commands once
in the PC's graphical user session.

## First local activation

Use the existing checkout at `~/Documents/GitHub/9to1`. Read `AGENTS.md` and
`docs/development/workflow.md`, reconcile its current revision and dirty work, and
retain the installed package. Close any existing Haven copy normally before the
first start; the helper refuses to adopt or stop an untracked Haven process.
Do not run two copies against one profile.

Python 3 and Linux x86-64 are required. Automatic close additionally requires
`xprop`, `wmctrl`, an X11-accessible window manager, and the same `DISPLAY` and
graphical session credentials as the first launch. Set the exact actual main
window title, commonly `9to1`; helper/modal windows are refused.

Initialize once with a stable, explicit development profile. These commands
choose a dedicated profile and leave the installed app's data untouched. To use
an existing profile instead, the local worker first observes its actual launch
configuration or owned process's `HAVEN_DATA_DIR` (inspect only that value, not all
environment credentials), then records that observation. A configurable path is
not proof of the installed app's actual profile.

```bash
cd ~/Documents/GitHub/9to1
git rev-parse --show-toplevel
git rev-parse HEAD
# Match the existing coordinator's exact approved source handoff; preserve WIP.
mkdir -p -m 700 "$HOME/.local/share/9to1-development/profile"
python3 -B eng/development/live_preview.py init \
  --state-root "$HOME/.local/state/9to1-live-preview" \
  --data-directory "$HOME/.local/share/9to1-development/profile" \
  --window-title '9to1' \
  --profile-observation 'Local worker selected this dedicated development profile; installed profile is untouched'
```

Initialization requires an existing canonical owner profile path (no symlink
parents) and local observation note, and refuses an existing configuration.
State is private (0700/0600),
outside Git. Runtime directories and application data must be separate from
launcher state and from each other. Existing profile permissions are untouched.

## Build, approve, reload

The local worker uses its **existing coordinator-admitted SDK allocation and
guarded incremental build**, serializing through the existing SDK lock. Preserve
the current source/manifest, memory, disk, process and native resource guards,
normal CUI compilation, dependencies and `-m:1`/disabled shared compilation.
The helper deliberately has no `dotnet watch` or arbitrary build-command option:
an automatic unguarded compiler/restart would evade these boundaries.

The normal owning command must be `build` or `publish` of
`9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj`, with an explicit
absolute `-o`/`--output` pointing at a **new** runtime directory, for example
`~/.local/share/9to1-development/runtimes/COMMIT-BUILD-ID`. Retain the real guarded
build receipt. Its `result` must be `PASS`, `exit_code` zero,
`admission.result` `PASS`, and `source_revision` the exact source revision. Do not
manufacture these fields if the current local guard does not supply them; reconcile
that receipt with the existing coordinator before activation.

This is a **local trust boundary**: the existing trusted guard/worker supplies the
receipt. The helper checks its hash, fields, command order/output path and any
retained source-manifest pin; it does not authenticate a remote issuer, prove that
a locally fabricated JSON represents execution, or re-observe historical source
bytes. The actual guard/SDK allocation remains authoritative. A receipt excerpt
has this shape (retain the real record; do not create evidence from this example):

```json
{
  "result": "PASS",
  "exit_code": 0,
  "source_revision": "ACTUAL_FULL_SOURCE_SHA",
  "admission": {"result": "PASS"},
  "command": ["/observed/local/dotnet", "build", "9to1 Workspace/shared/src/Haven.Desktop/Haven.Desktop.csproj", "-m:1", "-p:UseSharedCompilation=false", "-o", "/absolute/new-runtime"]
}
```

After the build and all its writers have finished, freeze only that newly created
runtime with `chmod -R a-w /absolute/new-runtime`. Never freeze a live checkout,
shared build cache, existing profile, or an output directory still used by a
compiler. A non-single-file self-contained linux-x64 publish is the convenient
portable layout; a normal framework-dependent build also needs its matching
installed .NET runtime on the PC. Retain the full ordinary output, including
`Haven`, `Haven.dll`, dependency/runtime configuration files and native assets.

Prepare a create-new approval file. All placeholders below are actual observed
paths/hashes from the successful owning build, not a substitute for running it:

```bash
python3 -B eng/development/live_preview.py prepare \
  --runtime /absolute/new-runtime \
  --build-receipt /absolute/private/build-receipt.json \
  --receipt-sha256 ACTUAL_RECEIPT_SHA256 \
  --source-revision ACTUAL_FULL_SOURCE_SHA \
  --output "$HOME/.local/state/9to1-live-preview/approved-BUILD-ID.json"
```

Preparation verifies the receipt identity and exact output path, ELF x86-64
apphost, ownership, immutable modes, complete file set and every file's SHA-256.
It refuses symlinks, mutable slots, mismatched receipts and overwriting approval
files. It does not claim native or independent acceptance.

For the first approved runtime:

```bash
python3 -B eng/development/live_preview.py start \
  --state-root "$HOME/.local/state/9to1-live-preview" \
  --manifest "$HOME/.local/state/9to1-live-preview/approved-BUILD-ID.json"
```

After one bug is fixed, repeat the admitted incremental build/approval step for a
new immutable slot, then reload the **same development copy and profile**:

```bash
python3 -B eng/development/live_preview.py reload \
  --state-root "$HOME/.local/state/9to1-live-preview" \
  --manifest "$HOME/.local/state/9to1-live-preview/approved-NEXT-BUILD-ID.json" \
  --timeout 90
python3 -B eng/development/live_preview.py status \
  --state-root "$HOME/.local/state/9to1-live-preview"
```

Reload validates the candidate before touching the running app. It binds the
original PID, Linux start ticks, session and executable to exactly one normal X11
window using `_NET_WM_PID` and exact `_NET_WM_NAME`, excludes transient windows,
rechecks identity, and requests ordinary `wmctrl -ic` window close. Unsaved-work
prompts remain the application's own. A tiny detached guardian waits only for
this helper's original app process and retains its real exit code; it never
signals or restarts anything. Reload requires that original exit code to be zero,
then revalidates the candidate and launches it. There is no `kill`, `pkill`,
signal fallback, timeout override or automatic acceptance.

If closing is cancelled or the timeout expires, the old copy remains owned and
the candidate is not launched. Resolve the prompt and repeat reload. If the
session lacks supported X11 controls, close the owned app manually, wait for its
recorded clean exit, then run reload. A failed exit, lost guardian receipt,
changed PID/executable, multiple matching windows or another Haven process blocks
replacement and requires local review. If writing a launch receipt fails after
starting a child, the error status exposes its original identity and the guardian
continues waiting/reaping it; manual reconciliation is required. Do not delete state/locks or substitute
another process to make a refusal pass.

`status` reports the **launched** source revision, manifest hash, apphost/DLL
hashes, original process identity, configured profile and the worker's observation
note. It labels that profile `OPERATOR_RECORDED_NOT_APP_CONFIRMED`. It does not infer the running
version from a later Git checkout. Per-run launch, exit and log files remain
under the private state's `runs/` directory; old builds and failures are retained.
No automatic runtime-manifest watcher is enabled.

### Explicit recovery after a failed process exit

Ordinary `start` and `reload` still refuse a nonzero exit. If a local coordinator
has inspected the retained failure, addressed its cause and verified that a
specific replacement is safe to try, `recover-failed` records that **manual
reviewed restart**, not a successful normal close/reload. It never closes or
signals a running application and requires the failed original process to be
gone. Installed/untracked Haven processes still block activation.

The local review receipt must use schema
`9to1.live-preview-failed-run-review/v1`, `result: RETRY_SAFE`, the exact
`failed_run_id`, `failed_exit` object (`path` and actual `sha256`), exact
`candidate_manifest_sha256`, and a nonempty `reviewer` and `reason`. These fields
are a trusted local review input, not remote authentication or authority created
by the launcher. Do not manufacture a review to bypass an unresolved failure.
The replacement still needs its genuine guarded owning build/immutable approval
and current execution admission. Preserve the failed run and its real exit code.

```bash
python3 -B eng/development/live_preview.py recover-failed \
  --state-root "$HOME/.local/state/9to1-live-preview" \
  --manifest /absolute/approved-REPAIRED-BUILD.json \
  --review-receipt /absolute/actual-local-review.json \
  --review-sha256 ACTUAL_REVIEW_SHA256
```

Review is bound to the exact failed exit and candidate before any activation.
An append-only receipt under `recoveries/` retains the review; all original
`runs/` launch, log and nonzero-exit files remain unchanged. The configured
profile stays the same. This operation does **not** satisfy the separately
required demonstration of a usable app's normal close/reload and native retest.

## Local worker handoff

Give the existing authorized local worker this instruction:

> Work in `~/Documents/GitHub/9to1` and preserve its dirty source, user data,
> installed package and existing coordinator. Reconcile the exact source and
> process before starting. Set up the existing canonical native app once through
> `live_preview.py`, using the owner's observed profile. For each reported bug:
> reproduce in this development copy, fix its owned source, run the affected
> checks and existing guarded incremental owning build into a new immutable slot,
> retain its genuine receipt, prepare and reload that slot through normal close,
> then report the loaded revision/hashes and actual result for the same repro.
> Ask the owner to retest that one fix. Do not make a new installer for each bug,
> start a second coordinator or model service, bypass a close veto, discard work,
> or describe source preparation as a successful PC connection or native pass.

## Focused checks

```bash
python3 -B -m unittest discover -s eng/development -p test_live_preview.py -v
```

These use real tiny ELF child processes and mocked X11 property/close responses.
They cover retained loaded identities, profile/history preservation, clean exit
before replacement, failed exits, close veto/timeout, unrelated/reused PID
refusal, immutable manifests and build-receipt gates. They do **not** establish
actual Haven close behavior, PC activation, GNOME/Wayland compatibility, native
workflow persistence, or independent acceptance; those remain local smoke work.
