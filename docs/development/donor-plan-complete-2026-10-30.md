# Donor plan, complete revision — 30 October 2026 (revises the earlier 9-item plan)

My earlier plan covered a minority of the donor set. The new audit
(`eng/development/donor_coverage.py`, this repository) re-reads the canonical specification, checks each
designation at its recorded line, and compares it with `eng/donor-sources.json` and `.gitmodules`.
Result: **28 designations across 25 distinct donors; only 18 have any registry binding, and 7 of those
have no materialised source.** The counts are produced by the tool, not by me.

Evidence labels: **MEASURED** = verified on this PC today; **UPSTREAM-DRAWN** = read from the upstream
project today but not vendored; **UNVERIFIED** = licence not yet read from a pinned tree; **NOT RUN** =
no execution evidence.

## 1. The rule that decides everything below (owner, 30 October 2026)

Verdict follows **what 9to1 actually modifies**, measured from patch files, our own commits and observed
working-tree dirt — not from whether the donor is "an app" or "an engine". Worked examples the owner gave:

- Files (donor) is an app, 9to1 Files extends its features and replaces its UI with CUI ⇒ **fork**.
- Firefox is an app, 9to1 Haven extends Firefox and replaces its UI with CUI ⇒ **fork Firefox**.
- Gecko is the engine inside that browser and we do not modify it ⇒ **do not fork**; include it prebuilt.
- Rnote has an engine and we modify that engine separately ⇒ **fork the engine**.
- A CUI wrapper counts as a modification, unless the donor is literally an unmodified engine used
  inside our own process boundary.

Distribution: **open source by default**, proprietary only where open-sourcing creates a specific risk
(safeguard bypass, malicious account-service requests, monetary risk, sensitive-information exposure,
infrastructure security risk, third-party IP/confidentiality), and only where that risk cannot be
mitigated by secure design or server-side validation, minimum necessary portion.

## 2. Designated donors the repository has never declared

These are mandated by the specification and absent from `eng/donor-sources.json`:

| Spec line | Owning product | Donor | Licence (state) | Verdict under the rule | Why |
|---|---|---|---|---|---|
| 1816, 1830 | CUI framework | **Avalonia** | MIT, notice kept in-tree (MEASURED) | **FORK — already true, recorded only today** | Spec: "CUI is derived from a pinned Avalonia source fork" and "MUST obtain and include the selected appropriate Avalonia source revision". Measured 21 modified + 21 of our own files. Fork created today: `CroakyJake12/Avalonia`, branch `9to1/cui-framework-20261030`, tip `b5bdd8cb9a`, three reviewable commits, 21/21 files byte-identical to our tree. |
| 4515 | Dulche runtime | **Strata** | UNVERIFIED (no upstream identified yet) | **UNRESOLVED — must not guess** | Spec: "Dulche MUST carry a maintained Strata fork rather than treating a separately installed Strata application as a required user dependency." Today nothing in the repo carries Strata source, while this PC runs a Strata build as the resident model host. Identifying which upstream/revision that host came from is a prerequisite, and the resident host must not be stopped or replaced to find out. |
| 14523 | Games | **Godot** | MIT (UPSTREAM-DRAWN, GitHub metadata) | **FORK** | Spec: "MUST be based on a maintained fork of Godot as its primary game-engine donor". No Games app source exists yet (`9to1 Workspace/Games` is empty). |
| 14525 | Games | **Blender** (modelling/rendering toolset) | COPYING names the GNU GPL; GitHub reports NOASSERTION, so the exact file scope is **UNVERIFIED** | **FORK** (deep integration ⇒ modification) | Spec: "integrate a maintained Blender-derived modelling/rendering toolset deeply enough that modelling feels like part of the same product". Upstream repo ~1.4 G (UPSTREAM-DRAWN). |
| 14982 | Mini Computer | **VirtualBox** | GPL-3.0 (UPSTREAM-DRAWN) | **FORK** | Spec: "MUST fork and maintain the latest appropriate VirtualBox source". No Mini Computer VM engine source exists. |
| 16038 | Maps | **CoMaps** | OsmAnd family, **UNVERIFIED** at file level | **FORK** | Spec: "The primary mature donor is the latest appropriate CoMaps source"; 9to1 Maps fork/adaptation must preserve offline maps/POI behaviour. |
| 16203 | Launcher | **Lawnchair** | **UNVERIFIED** (the repository could not be resolved under that name today) | **FORK** | Spec: "Lawnchair is the primary donor and mature behavioural baseline for Launcher". |
| 6746, 6768 | Build (source collaboration) | **Gitea** | MIT (UPSTREAM-DRAWN) | **FORK** | Spec: "Gitea is the primary donor and must be forked from the latest appropriate source at implementation time." |
| 6748 | Build (source collaboration) | **Gitnuro** | GPL-3.0 (UPSTREAM-DRAWN, ~13 MB) | secondary donor/reference; **FORK only if used and modified** | Spec permits it for local Git UX "where its licence and architecture permit" — that permission test still has to be done. |
| 7491 | Build (site workspace) | **cakemods** site plugin | **UNVERIFIED** (two candidate repositories found, neither declares a licence) | **BLOCKED on licence** | A donor with no licence statement cannot be adopted; needs the rights holder's position or an explicit 9to1-native replacement. |
| 12219 | Connect | **Element + Matrix** | Element AGPL-3.0; `matrix-rust-sdk` Apache-2.0 (UPSTREAM-DRAWN) | client **FORK** (we re-UI and extend); Matrix server side is an external service, not a source donor | AGPL is workable inside the open client; the boundary matters for the paid/hosted side, so review before any linkage. |
| 12221 | Connect / Mail mode | **Thunderbird** | MPL-2.0 expected, **UNVERIFIED** at file level | **FORK** for the app layer; engine/library behaviour may be embeddable | Spec keeps Thunderbird as the mature email-behaviour reference and explicitly rejects its visual shell. |
| 16694 | 9to1 OS | **GNOME system-monitor tooling** (Task Manager baseline) | GPL family, **UNVERIFIED** per component | **FORK** (we rebuild it in CUI.NET and extend it) | Spec: "Task Manager is rebuilt in CUI.NET using the appropriate Ubuntu/GNOME system-monitor tooling as donor baseline". |

## 3. Declared in the registry but not designated by the specification's own wording

The same audit reports these as "binding with no specification designation found". Some are genuinely
designated in prose my line-pattern didn't match; others contradict the specification and must be
settled by the owner rather than left in the registry:

| Registry binding | Spec wording found today | Status |
|---|---|---|
| `libreoffice` (Write, Present, Data) | designated at 8387/8651/8761/10318/10320 | designated; audit keys differ (`libreoffice-writer/-kit/-impress-draw/-calc`) — map them to one registry identity with per-app pins |
| `code-oss`, `vscodium` (app key **Dev**) | **no occurrence of "VS Code"/"VSCodium"/"Code - OSS" anywhere in the specification** (measured: 0 matches) | the specification treats **Build as one application** and Dev as a workspace, so both the donor designation and the app key need an owner decision |
| `eds`, `libical` (Planner) | Planner section says it is **first-party, "No external application donor is currently normative"** (line 11897) | direct contradiction: registry declares donors the specification declines. Owner decision, not mine |
| `glycin`, `loupe` (Pictures) | Picture section produced no donor sentence in this sweep | verify or reclassify |
| `gstreamer`, `ges` (Wave, Motion) | no donor sentence in this sweep | verify or reclassify |
| `libvterm` (Terminal) | no donor sentence in this sweep | verify or reclassify |
| `llama-cpp` (Dulche) | designated at 4485 | declared; **EMBED** (measured: our `Dulche/llamacpp` tree has 0 upstream C/C++, pinned `53ed051ce5e8…`, CI builds the binary) |
| `files` | designated at 10804 | declared, loose copy, base proven by tree identity |

## 4. What exists today, precisely

| Donor | Materialised | Consumed by a build? | Verdict |
|---|---|---|---|
| Avalonia | loose vendored tree + fork branch (created today) | **yes** — `CakeOS.Cui.AvaloniaFork.csproj` compiles it | FORK |
| Rnote (Canvas) | gitlink `8cac558f` + fork branch `9to1/canvas-engine-20261030` (`44ff1089`) | yes, via `rnote-poc` (Rust; no toolchain on this PC) | FORK (engine crates) |
| Rnote (Boards) | gitlink `1a728d6a` | no | NOT CONSUMED |
| AppFlowy Board | gitlink `804d7898` | no (adapter is pure C#) | NOT CONSUMED |
| Firefox / Gecko | gitlink `5836a062`, 4.1 G | **no engine at all on this PC** (both system chromium paths absent) | app FORK, engine EMBED-prebuilt |
| DuckDB | gitlink `d8cdaa33` (tag v1.5.5) | the C++ source: no; the Python wheel: yes, unpinned | EMBED |
| Files (WinUI) | 2,351 loose files, base proven at `21d407b5` | only as reference (WinUI cannot be app source) | FORK |
| llama.cpp | no source, pinned revision recorded | binary produced by CI | EMBED |
| Avalonia.DBus, XamlX | gitlinks inside the Avalonia tree | yes | EMBED |
| 17 others (LibreOffice, VSCode, GStreamer/GES, glycin/loupe, libvterm, eds, libical, llama.cpp source, Wine, WinBoat, gnome-shell, mutter) | **nothing on disk, no gitlink** | no | classified but unmaterialised |

The pre-existing donor gate `eng/check-donor-sources.py` fails **17 issues across 23 bindings**, all
"no app-local source tree" for exactly those unmaterialised ones. Neither the gate nor the new coverage
audit runs in any CI workflow (measured: no workflow references them).

## 5. Ordered plan (each item independently approvable)

1. **Wire the audits in before adding anything else.** Run `donor_coverage.py audit` and
   `check-donor-sources.py` as owning checks plus a pre-export gate. Right now a donor can be missing
   from the registry, from disk and from CI simultaneously and nothing notices.
2. **Avalonia — finish what today started.** Add our authored files to the fork branch
   (`CakeOS.Cui.AvaloniaFork.csproj`, `build/upstream-resource-targets/*.props|targets`); keep generated
   interop and browser assets as build output. Then switch the vendored directory to a submodule pinned
   at the fork tip and build the CUI framework here (dotnet only). Register Avalonia as an upstream with
   per-app pins, so the gate can see the framework we actually compile.
3. **Rnote engine (Canvas).** Move `rnote-poc/Cargo.toml` + `Cargo.lock` onto `44ff1089`, retire the ARM
   vendor-patch stage in favour of verifying the fetched revision already contains the reviewed changes,
   and move the gitlink in the same change. Needs a Rust toolchain decision: this PC has none.
4. **Files.** Pin the vendored copy as a submodule at `21d407b5`; stop overwriting the donor's `AGENTS.md`
   with ours; start the parity manifest (three-root navigation, sorting/grouping, tags, archives, volumes)
   modelled on `Boards/DonorParityManifest.md`.
5. **DuckDB.** Pin the wheel that actually runs: `Data/workers/requirements.in` + hash-locked
   per-platform locks + an agreement check against `REQUIRED_DUCKDB_VERSION` + failure-mode tests.
6. **Haven engine.** Engine-artifact manifest for prebuilt Firefox (x64 here, `linux-aarch64` on the ARM
   target) with signature- and digest-verification; remove/label the `/usr/bin/chromium` lookup; then the
   narrow CUI-hosting prototype. Gecko engine scopes stay pristine (the census reports any change inside
   them as unresolved).
7. **New forks that the specification already requires**, in dependency order for the priority apps:
   Gitea (Build), Element/Matrix + Thunderbird (Connect/Mail), then the heavy engine set —
   Godot + Blender (Games), VirtualBox (Mini Computer), CoMaps (Maps), Lawnchair (Launcher),
   GNOME system-monitor (OS). Each needs a controlled fork, a pin at the consumed revision, a licence
   read from the pinned tree, and a patch ledger before any claim of integration.
8. **Resolve the four blocking unknowns** rather than coding around them: Strata's upstream identity and
   our modifications to it; cakemods' licence; Thunderbird/CoMaps/Lawnchair/GNOME component licences at
   file level; whether `code-oss`/`vscodium`, `eds`/`libical`, `glycin`/`loupe`, `gstreamer`/`ges` and
   `libvterm` remain designated donors at all, given the specification's wording.
9. **Cost guard, because these are big.** Measured upstream sizes: Godot ~1.85 G, Blender ~1.39 G,
   Thunderbird ~4.7 G, VirtualBox ~1.47 G, OsmAnd/CoMaps ~1.47 G, Gitea ~0.36 G, Element-web ~0.6 G,
   Gitnuro ~13 MB. Free disk here is 542 G. Unmaterialised donors must stay pinned-but-not-checked-out
   until an app consumes them, or every guarded export grows by gigabytes per candidate.

## 6. What I am not claiming

No donor integration is proven by this document. Nothing here is compiled, run natively, packaged or
accepted. Registry/gate/coverage changes are records and checks, not behaviour. The specification remains
the product authority; where the registry and the specification disagree, the specification wins and the
disagreement is listed for an owner decision rather than resolved silently by me.
