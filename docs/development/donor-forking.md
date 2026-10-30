# Donor forking plan — rule, classification, mechanics

30 October 2026, after the owner's engine decision. Evidence labels: **SOURCE-INSPECTED** = read at
the named revision in this repo; **MEASURED** = produced by a command run here today; **NOT RUN** =
no execution evidence yet; **OWNER DECISION** = stated by the owner.

## 1. The rule (OWNER DECISION, 30 October 2026)

Owner's wording, in order of refinement (the last pair of examples is the operative one):

1. "Embed what we aren't modifying, fork what we are."
2. "A CUI wrapper counts as modify."
3. "…unless it's literally just an engine used within another process that isn't modified."
4. Final clarification, stated as three named cases (owner, 30 October 2026):
   - *"Files (donor, F) is a files app. 9to1 Files is a files app. We are extending the features of F
     within Files, and replacing its UI with CUI. Therefore **fork**."*
   - *"Firefox is a browser. 9to1 Haven is a browser. We are extending the features of Firefox within
     Haven, and replacing its UI with CUI. Therefore **fork**."*
   - *"Gecko is an engine used within a browser. 9to1 Haven is using it within its browser. We are not
     modifying Gecko, so **do not fork**."* (earlier phrasing: "don't fork source, just include it
     prebuilt within the program")

**The test is about which artifact plays which role, not about how much we change.**

| Role of the donor artifact | What 9to1 does to it | Verdict |
|---|---|---|
| **Application** (a product with its own UI: Files, Firefox, LibreOffice, VSCode, Rnote-the-app) | we extend its features inside our equivalent app and replace its UI with CUI | **FORK** — controlled remote, our commits on a named branch, gitlink pinned at the consumed revision |
| **Engine inside an application** (Gecko inside a browser, DuckDB inside a query worker, GStreamer inside an editor) | we use it as-is from our own code, changing nothing in it | **DO NOT FORK** — embed it, preferably as upstream's **prebuilt** signed binary, pinned + digest-verified |
| Declared but not used | — | **NOT CONSUMED** — recorded intent only, no checkout, no claim |

Two consequences that are easy to get wrong, so they are written down:

- **Forking Firefox is not forking Gecko.** In one forked source tree, the *application* layer
  (browser chrome, tabs, preferences, about/pages, extension surfaces, per-app behaviour) is ours to
  change, while the *engine* directories must stay byte-for-byte upstream. The fork is what lets us
  extend browser features and drop Firefox's UI; engine pristineness is what lets us treat the engine
  as an unmodified embedded component — and it is **checkable**, so §4 rule 9 enforces it.
- **Replacing a UI in CUI is a change to the application layer, not to the engine.** If we end up
  patching the engine itself (as we already do to Rnote's engine crates), the engine stops being an
  unmodified embedded component and must be forked and labelled accordingly. Rnote is the live
  example of that, and it is already forked.

## 2. Measured current state (MEASURED, 30 October 2026)

- `.gitmodules` declares **26** donor entries; only **7** have a real gitlink in HEAD. The other
  **19** are stubs that `git submodule update` can never fetch: no gitlink and no on-disk source.
- All 19 declared **controlled-fork remotes already exist** on GitHub (verified with `git ls-remote`;
  branch counts e.g. libreoffice 754, vscode 5,279, llama.cpp 805, gecko-dev 621, gnome-shell 303,
  mutter 318). So the org-wide fork scaffolding exists; what is missing is pins, provenance and
  consumed revisions — not remote availability.
- Checked-out donor content now costs **~4.5 G** inside the repo: Gecko 4.1 G, DuckDB 290 M,
  Canvas/Rnote 50 M, Boards/Rnote 53 M, AppFlowyBoard 1.9 M. This matters because the guarded export
  path enumerates submodule trees.
- `framework/CUI/vendor/Avalonia` is a **loose 106 M / 5,877-file copy** with no git of its own;
  provenance is only `SOURCE.md` + `NOTICE.md` (SOURCE-INSPECTED). It is genuinely used and genuinely
  modified — i.e. a fork by the rule, recorded as a folder copy rather than a pinned fork.
- `9to1 Workspace/Files/Source/Files` is **2,351 loose vendored files** (MPL-2.0 + MIT licence files
  present), no gitlink (SOURCE-INSPECTED).
- The Canvas bridge consumes Rnote through `rnote-poc/Cargo.toml` as
  `git = "https://github.com/flxzt/rnote", rev = "8cac558f…"` — **upstream**, not our fork — with two
  patch files (`0001-keyed-stroke-operations.patch`, `0002-headless-tasks-and-split-lineage.patch`)
  that "the ARM producer vendors the locked graph, applies both patches" at build time
  (`Canvas/rnote-poc/PROVENANCE.md`). That is exactly the "depends on a random copy of the source"
  pattern, and by the rule it is a modification, so it must resolve to a fork revision.

## 3. Classification

| Donor / engine | Used by | Verdict under the rule | Evidence and what follows |
|---|---|---|---|
| **Files (WinUI Files app)** | Files | **FORK** | Owner's worked example: adds features the donor lacks. Currently 2,351 loose files with no pin → needs a controlled fork branch + gitlink at the consumed revision. Note: we cannot compile its WinUI/C# as app source under the CUI rule, so the fork is for behaviour/feature lineage and shared non-UI logic, with the UI rebuilt in CUI. |
| **Avalonia fork** | CUI framework | **FORK (already)** | Modified and used, but recorded as a loose tree. Needs: real fork remote, upstream base commit recorded, our commits visible as commits, patch ledger. Highest integrity gap of the set. |
| **Rnote (Canvas)** | Canvas | **FORK (already exists, must be consumed)** | We patch it and add our serialization commit → modified. Action: land both patches + our commit on a fork branch, then point `rnote-poc/Cargo.toml` and the gitlink at that **fork** revision instead of upstream + vendor-time patching. Owner chose to keep Canvas at v0.14.2 and Boards' pin at the v0.15 line for now. |
| **Rnote (Boards)** | nothing today | **NOT CONSUMED** | No Boards build links it; pin records documented intent only. |
| **AppFlowy Board** | nothing today | **NOT CONSUMED** | `AppFlowyBoardEventAdapter.cs` is pure C# command mapping, links no Flutter/Dart. If donor code is ever used: it is dual AGPL-3.0 OR MPL-2.0 → elect **MPL-2.0**, record it. |
| **Gecko / Firefox** | Browse | **EMBED — prebuilt, no source fork** | Owner's worked example. Measured clean upstream pin (`5836a062…`, an upstream Mozilla changeset, author `florian@queze.net`), no `.patch`, no mozconfig anywhere under Browse. Consequence: the 4.1 G source checkout is **provenance only**, not a build input → set `submodule…update = none` so it stops riding in every export, and instead add a versioned engine-artifact manifest (exact release, channel/candidate build, locale, architecture, artifact path, source changeset, signature identity, digest). |
| **Chromium** | Browse (Linux today) | **EMBED — prebuilt** | Never modified, driven out-of-process. But **no official Linux ARM64 build exists** in Chrome for Testing (research pack, upstream-documented), while Mozilla publishes signed `linux-aarch64` candidates (upstream-documented). So Chromium is a dev-only x64 engine here, and Gecko is the ARM64 engine route. The current `/usr/bin/chromium` lookup must go or be labelled dev-only: on this PC both `/usr/bin/chromium` and `/usr/bin/chromium-browser` are absent (MEASURED), i.e. Browse has no engine at all right now. |
| **DuckDB** | Data | **EMBED — prebuilt wheel** | Unmodified. What Data actually loads is the Python wheel, not the vendored C++ (SOURCE-INSPECTED: `workers/duckdb_worker.py` requires exactly `1.5.5`). Needs `workers/requirements.in` + per-platform hash-locked wheels + an agreement check against `REQUIRED_DUCKDB_VERSION`; PyPI publishes a cp312 manylinux **aarch64** duckdb 1.5.5 wheel (upstream-documented; not downloaded or hash-verified here). |
| **llama.cpp** | Dulche / Thunder | **FORK (pending review)** | Thunder is our integrated engine with reviewed enhancements; if those enhancements touch its source, the rule says fork (remote exists, 805 branches). Needs the actual enhancement inventory before the label is final. |
| **LibreOffice** (Write, Present, Data) | planned | **FORK if used** | Re-UI + added features ⇒ modified ⇒ fork. Feasibility is unmeasured and the build cost is large; do not treat the fork remote's existence as progress. |
| **VSCode / VSCodium** | Dev | **FORK if used** | Same reasoning. |
| **gnome-shell, mutter, wine, winboat** | 9to1 OS | **FORK** | We change them; remotes exist. |
| **GStreamer, GES, libvterm, glycin, loupe, evolution-data-server, libical** | Wave, Motion, Terminal, Picture, Planner | **EMBED now, fork on first patch** | Unmodified engines/libraries. Trigger rule below. |

## 4. Mechanics that make the rule enforceable

1. **Fork-on-first-change trigger.** The moment a change of ours lands in donor code, it must exist as
   a commit in our controlled remote on branch `9to1/<app>-<purpose>-<yyyymmdd>`. Applying patches to
   a fetched upstream copy at build time is not allowed as the durable state — it can be a temporary
   build step only, with the fork branch as the authority and an expiry.
2. **Pin at the consumed revision, never the aspirational one.** Read the version from the donor's own
   manifest at that commit (as done for Rnote v0.14.2 and DuckDB tag v1.5.5). If two apps share a
   donor, they share one pin, or the divergence is written down — Rnote is currently allowed to
   diverge by owner decision.
3. **Pins are immutable.** Never force-push a branch a gitlink points at; new work gets a new branch
   and a new pin.
4. **Patch ledger per donor**: file, reason, upstream bug/PR reference, expiry condition (drop when
   merged upstream).
5. **Licence ledger per donor**: licence read from the pinned tree (not reputation), election where a
   donor is dual-licensed, notices carried into the package at
   `/usr/share/doc/<actual-binary-package-name>/copyright`.
6. **Cost guard**: unconsumed donor trees are pinned but **not checked out** (`update = none`),
   otherwise exports grow by gigabytes per candidate (Gecko alone is 4.1 G).
7. **Prebuilt-artifact guard**: an embedded engine is shipped from a manifest containing exact version,
   channel/candidate build, architecture, locale, artifact path, source changeset, publisher signature
   identity and digest. A checksum downloaded next to the binary is not publisher authentication, and
   no digest may be recorded as "verified" without a real verification receipt.
8. **Drift check**: a census command comparing `.gitmodules` entries, gitlinks in HEAD, on-disk
   presence and consumed-by-build status, so "declared" can never again look like "integrated".

## 5. Ordered actions

1. **Avalonia provenance** (integrity gap): identify upstream base commit, record it, list our changes
   as commits or a patch ledger, and point the vendor entry at a controlled fork.
2. **Canvas/Rnote**: land patches + our serialization commit on a fork branch off v0.14.2; repoint
   `rnote-poc/Cargo.toml` and the gitlink to that fork revision; rebuild the bridge against it.
3. **Gecko**: mark the submodule `update = none`; add the engine-artifact manifest for prebuilt
   Firefox (x64 for this PC, aarch64 for the ARM target); remove or label the system-binary lookup.
4. **DuckDB**: add `requirements.in` + hash-locked per-platform wheels + agreement check.
5. **Census tool**: commit the declared/pinned/present/consumed checker and record its output.
6. **Files**: convert loose copies into a pinned controlled fork at the consumed revision.
7. **Thunder/llama.cpp**: inventory our enhancements, then label embed vs fork with evidence.

Integration note for prebuilt Gecko (NOT RUN, flagged honestly): Mozilla's prebuilt Firefox is a
browser, not a drop-in in-process rendering library. "Include it prebuilt, unmodified" is satisfiable
by driving it out-of-process and hosting its surface, but the CUI-hosted surface, input, DPI, IME,
accessibility, profile isolation and crash recovery each need a narrow real prototype on this PC and
on the ARM target before any claim of Browse integration. Distribution of the unmodified Mozilla build
is straightforward; Firefox **branding/trademark** is separate from software-licence compliance and
needs its own mark when we ship a rebranded 9to1 browser.
