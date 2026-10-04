# Component release evidence

This record identifies the authoritative source trees materialized in this
checkout. It does not claim that a Debian package or installed VM artifact
exists when the source branch does not provide one.

## Data

- **Source:** `apps/Data/` from `e3a195b032bd57b4bf85ff82ff8f6a5e7166d1c6`
  (`data-calc-duckdb-first-slice`).
- **Dependencies:** .NET SDK, Python 3, LibreOffice Calc headless with
  `libreoffice-calc-nogui`, `libreoffice-core-nogui`,
  `libreoffice-common`, `python3-uno`, and DuckDB 1.5.5 as described by
  `apps/Data/README.md`.
- **Launcher:** none supplied by the authoritative tree. The C# project and
  worker entry points are not desktop launchers.
- **Package:** no package recipe or package artifact is supplied; package
  built/installable status is **UNKNOWN**.
- **Smoke contract:** `dotnet run --project
  apps/Data/Tests/HavenOS.Data.Smoke.csproj -c Release`, the Python worker
  tests, and the runtime projects listed in `apps/Data/README.md`.
- **Offline:** the source documentation reports disposable Ubuntu runtime
  proof, not approved CakeOS VM or offline application proof.

## Present

- **Source:** `apps/present-engine/` from
  `b37a1e1a9ab9cbd03a9269438f3e15acb6921e65`
  (`migration/present-libreoffice-foundation`).
- **Dependencies:** CMake 3.20+, C++20 compiler, `libreofficekit-dev`,
  `nlohmann-json3-dev`, and a no-GUI LibreOffice Impress/Draw runtime.
- **Launcher:** none supplied. `cakeos-present-worker` is an out-of-process
  protocol worker, not a desktop launcher.
- **Package:** no package recipe or package artifact is supplied; package
  built/installable status is **UNKNOWN**.
- **Smoke contract:** configure and build with the CMake project, run CTest,
  and use the worker protocol smoke tests described in
  `apps/present-engine/README.md`.
- **Offline:** runtime proof is documented for disposable Ubuntu CI only;
  approved VM and functional offline application status are **NOT PROVEN**.

## Wine compatibility

- **Source:** `compatibility/wine/` from
  `4e513d27ca6099ab7351c3d528256fdc0e26b4a1`
  (`feature/windows-compat-broker`).
- **Dependencies:** Python 3, Bubblewrap, per-user systemd
  (`systemctl`, `systemd-run`, `journalctl`), Wayland, and a separately
  managed versioned Wine runtime. The broker deliberately does not install or
  bundle Wine.
- **Launcher:** no desktop launcher is supplied. The supported entry points
  are the CLI and the rendered user service template
  `compatibility/wine/systemd/haven-compatd.service.in`.
- **Package:** `compatibility/wine/PACKAGING.md` is a packaging contract only;
  no package recipe or package artifact is supplied. Package
  built/installable status is **UNKNOWN**.
- **Smoke contract:** `compatibility/wine/acceptance.sh` renders and verifies
  the user unit and runs the read-only prerequisite audit; it does not install
  services, create prefixes, or launch Windows applications.
- **Application launch/offline:** no Windows application compatibility or
  offline functional claim is made by this slice.

## Write recovery

A bounded check of `git log --all --oneline -- apps/write apps/Write
apps/present apps/data compatibility/wine` and
`git ls-tree -r --name-only origin/main -- apps/write apps/Write apps/present
apps/data` found no Write source, package, launcher, or handoff artifact.
**BLOCKED — NO AUTHORITATIVE IMPLEMENTATION RECOVERED.** Write is not
inferred from unrelated components.

## Ubuntu cohort packaging

`.github/workflows/cohort-packages.yml` runs on Ubuntu 24.04 and invokes
`packaging/cohort/build-debs.sh`. It builds:

- `havenos-data_<version>_amd64.deb`, launcher
  `/usr/bin/haven-data-calc-worker`;
- `havenos-present_<version>_amd64.deb`, launcher
  `/usr/bin/cakeos-present-worker`;
- `havenos-wine-compat_<version>_amd64.deb`, user service
  `/usr/lib/systemd/user/haven-compatd.service`.

The workflow records per-package SHA-256 values in `SHA256SUMS`, dependency
metadata in `cohort.json`, and the GitHub artifact ID/digest in the release
record. `packaging/cohort/verify-debs.sh` extracts each package into a clean
root, checks installed paths, verifies the rendered Wine unit has no
placeholders, runs Python syntax smoke checks, and verifies checksums.
