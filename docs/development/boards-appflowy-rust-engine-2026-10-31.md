# Boards: CUI frontend wired to AppFlowy's own Rust engine — route, measured facts, decisions needed

Owner direction, 31 October 2026 (~21:30), in sequence: *"use the current boards ui and wire it up to appflowy's
rust code"*, *"and fix any layout issues"*, *"so all the appflowy features are there"*, *"basically keep cui
frontend, write it up to appflowy's code"*.

Required flow, pinned before coding: **the 9to1 Boards CUI stays the frontend; AppFlowy's Rust engine supplies the
features; the layout is then corrected in CUI.** Feature parity comes from the engine, not from re-implementing
board semantics in 9to1.

## Measured facts about the engine in the pinned donor snapshot

Snapshot: `9to1 Workspace/Boards/Source/AppFlowy` (AppFlowy 0.14.11 @ `5cf3a365dec0`, preserved on
`CroakyJake12/donor-appflowy`, tag `upstream-0.14.11-5cf3a365dec0`). The Rust layer is present and already preserved
inside `frontend/rust-lib/`:

| measurement | value |
| --- | --- |
| crates in `rust-lib` | 38 (incl. `lib-dispatch`, `dart-ffi`, `flowy-sqlite`, `flowy-storage`, `build-tool`) |
| Rust files / lines | **745 files / 105,922 LOC** |
| `flowy-database2` (board, grid, calendar, field types, filter, sort, group by, layouts) | **224 files / 34,056 LOC** |
| `flowy-folder` (page/space tree, view records) | present |
| `flowy-search` (+ `flowy-search-pub`) | present — **closes the "board search" gap I had listed** |
| `flowy-document` | 41 files / 4,918 LOC |
| pinned toolchain (`rust-lib/rust-toolchain.toml`) | **Rust 1.85** with clippy + rustfmt |
| `.proto` files in the snapshot | **0** — schemas are Rust derive-macro definitions, generated into the client (AppFlowy's `frontend/scripts/code_generation` is the generator path) |
| licence | repo root `LICENSE` is **AGPL-3.0**; crate manifests carry no separate `license` field, so AGPL applies |

Environment constraint measured: `MemAvailable` = **6 GiB** while this OpenCode session's resident model host holds
~45 GiB, and there is **no cargo/rustc/rustup installed**. A dependency-heavy `cargo check` of `flowy-database2`
(tokio, serde, protobuf, yrs, sqlite3, …) is exactly the class of job that has previously been bounded by the
memory guards, so it must be scheduled with a real budget, not started blindly against 6 GiB.

## The two boundaries this route has to respect

1. **Licence.** AppFlowy is AGPL-3.0. Linking it into the proprietary 9to1 app would make the whole work a covered
   work. The established precedent in this repo (Rnote, GPL-3.0-or-later) is a **separate process with a typed
   boundary and retained notices** — so the engine runs as an AppFlowy-engine sidecar that 9to1 talks to over the
   dispatch protocol, not as a library inside the Boards app. Owner decision `OPEN-2026-10-31-BOARDS-APPFLOWY-RUST-ENGINE`
   records this; nothing ships until the owner rules on it.
2. **Schema bridge.** There are no `.proto` files, so a .NET client cannot be generated from a schema file. Measured
   options, cheapest first: (a) run AppFlowy's own `code_generation` to emit the schema from the Rust macros and
   generate from that; (b) define a narrow typed C ABI over `lib-dispatch` in Rust (the owner explicitly allowed
   native Rust work: *"you can do stuff in the native rust"*) covering the calls Boards needs — create/open doc,
   list sections/views, list rows, create/rename/delete row, group by, filter, sort, layout settings, search; (c) a
   JSON facade in the sidecar. Any of these keeps the CUI frontend untouched.

## How it plugs into the existing app (no rewrite of the frontend)

The Boards frontend already talks to a session interface: `IRichBoardSession`
(`9to1 Workspace/Boards/app/IRichBoardSession.cs`) with `ContractSessionAdapter` as the current JSON-file
implementation, and the CUI surface binds to view-model state. The engine route is therefore **one new session
implementation** over the sidecar, selected behind the existing composition — the CUI documents, actions and
bindings stay as they are, which is what "keep cui frontend" means.

## Executable order

1. Frontend layout fixes on the current surface (free, do now): sidebar/content material separation using measured
   palette pairs, tab-strip backing and selected state, section-column and card geometry per the donor numbers.
2. Build feasibility proof with a real budget: install the pinned Rust 1.85 toolchain, then `cargo check -p
   flowy-database2 --locked` with a memory floor and the existing guarded-runner discipline; record PASS/PARTIAL
   honestly. Deferred: it must not run while `MemAvailable` is below the floor.
3. Schema decision (a)/(b)/(c) above, then the typed session boundary.
4. `AppFlowyEngineSession : IRichBoardSession` + composition switch, then the board surface shows engine state
   (sections, rows, fields, filters, sorts, layout settings, search).
5. Layout correction pass in CUI against the engine-backed surface, with on-screen captures and numeric pixel
   probes as evidence.

## Evidence level of this document

Everything above is measured from the pinned snapshot and this machine (file counts, LOC, toolchain pin, licence
text, `MemAvailable`, absence of cargo). No engine build has been attempted yet, so nothing about compiling or
running it is claimed. Independent acceptance stays paused; the reported-bug list still comes first for
reproduction work.
