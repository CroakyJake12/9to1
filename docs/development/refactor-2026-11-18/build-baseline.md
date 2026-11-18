# Build baseline for the 18 November refactor (machine: this PC, 18 Nov 2026)

Environment: `/home/jacob/.dotnet/dotnet` SDK 10.0.301 (PATH `dotnet` is 8.0.134); Linux, MemAvailable ~5.8–6.4 GiB with the resident model host running. All builds via `python3 -B eng/development/guarded_lowmem_build.py` (floor 1100 MiB) with `-m:1 -p:UseSharedCompilation=false`, serial.

| Lane | Command (guarded) | Result | Notes |
| --- | --- | --- | --- |
| Workflow guards (Python) | `python3 -B -m unittest discover -s eng/development -p test_workflow.py` | **OK — 160/160** (6.6s) | after AGENTS.md authority edits |
| Full `Haven.sln` Release | `dotnet build "9to1 Workspace/shared/Haven.sln" -c Release --no-restore` | **FAIL as expected on this host** — 280.8s, peak RSS 557 MiB, min MemAvailable 4262 MiB | three causes, all environmental not product: (1) several projects never restored here (missing `obj/project.assets.json`, e.g. Haven.Core.Tests, Haven.Connector); (2) `Haven.Desktop.Tests` targets `net10.0-windows10.0.19041.0` (Windows CI lane); (3) `Haven.Android` needs the `android` workload (not installed). No memory-guard stop occurred. |
| `Haven.Desktop` (shell, Linux net10.0) | restore OK 9.5s; then `dotnet build ... -c Release --no-restore` | **OK — 0 errors** in 156.8s; peak RSS 545 MiB, min MemAvailable 4513 MiB, guard never fired | the S1-relevant compile lane is green on this machine |

Compile evidence only: nothing here renders natively, packages or accepts anything.
