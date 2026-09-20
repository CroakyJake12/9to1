# Tonight's package-backed cohort

This is a package-admission record, not an image, VM, or release decision. A
candidate is admitted only when an immutable Debian artifact, its SHA-256,
installed entrypoint, runtime dependencies, and source provenance are all
recorded. `verify-admission.py` validates this metadata. Before installation,
`packaging/llamacpp/stage-cohort.sh` must verify the downloaded package bytes,
package names, architectures, and installed paths.

## Admitted

| Candidate | Accepted source | Immutable Debian artifact | Entrypoint and installed path | Runtime dependencies |
| --- | --- | --- | --- | --- |
| llama.cpp local inference runtime | `CroakyJake12/CakeOS@11be1b700022095e4b598768b5286adfe966f85a`, `runtime/llamacpp/`; upstream `ggml-org/llama.cpp@5266f24da75dc449bd56cbed7addb9c8e4a6a73e` | `haven-llamacpp-runtime_0.4.0+haven0.1_amd64.deb`; SHA-256 `13ea16e4ffa92d1e4b2f31a1a32ba00dc8802ec9077733d7db96a68dad92142d`; artifact `10031771129` / `haven-llamacpp-runtime-amd64` | `/usr/bin/haven-modelctl`; broker at `/usr/lib/haven/inference/broker.py`; server at `/usr/lib/haven/llama.cpp/llama-server` | `python3`, `libc6`, `libstdc++6`, `libgcc-s1`, `libgomp1` |
| HUI Linux graphical preview | `CroakyJake12/CakeOS@2a578502c31973fbefa3b3f82efa22d7ad11db53` on `platform/hui-linux-graphical-preview`; host source `HUI/LinuxHost/` | `haven-hui-preview_0.1.0+git2a578502c319_amd64.deb`; SHA-256 `104f8ff3929b8b0e1c90673804d7cfbe79a31ea5c2763dc040246a7ead53575a`; run `34157071097`, artifact `10031358716` / `hui-linux-graphical-package` | `/usr/bin/cakeos-hui-preview`; host at `/usr/lib/cakeos/hui-preview/cakeos-hui-linux-preview` | `libc6`, `libgcc-s1`, `libstdc++6`, `zlib1g` |

The llama.cpp package is model-free and does not auto-enable its user service.
The HUI preview is the pinned graphical preview, not an application package for
Data, Present, or any other app. Neither artifact establishes approved-VM
acceptance.

## Deferred

| Candidate | Accepted source / evidence | Admission blocker |
| --- | --- | --- |
| Data (Calc + DuckDB) | `apps/Data/` from `e3a195b032bd57b4bf85ff82ff8f6a5e7166d1c6`; source workers `workers/calc_worker.py` and `workers/duckdb_worker.py` | No desktop launcher or installed package proof. Required runtime is Python 3, `python3-uno`, `libreoffice-calc-nogui` (and its core/common runtime), plus exactly `duckdb==1.5.5`; no Debian package bundles or declares that binding. `HavenOS.Data.App.csproj` is a library, not a launchable app. |
| Present | `apps/present-engine/` from `b37a1e1a9ab9cbd03a9269438f3e15acb6921e65`; source worker entrypoint `src/worker.cpp` | The worker needs `libreoffice-impress-nogui` and `libstdc++6`; it is runtime-proven in CI but has no installed artifact or HUI app launcher. A worker binary plus README is not an application package. |
| Wine compatibility | `compatibility/wine/` from `4e513d27ca6099ab7351c3d528256fdc0e26b4a1`; prospective unit path `/usr/lib/systemd/user/haven-compatd.service` from `systemd/haven-compatd.service.in` | Required runtime is Python 3, Bubblewrap, a reachable user systemd manager, Wayland, and a separately managed executable Wine runtime. No Wine runtime, installed user-service proof, or Windows application launch proof exists. |
| Studio / Dev tool | `tooling/HavenOS.Studio/`; prospective launcher `/usr/bin/havenos-studio` in `packaging/build-havenos-studio-deb.sh` | The recipe declares `libc6`, `libgcc-s1`, `libstdc++6`, and `zlib1g`, but no immutable `.deb` or installation smoke is recorded. Its required publish directory is not package evidence. |
| Welcome | `apps/haven-welcome/`; prospective launcher `/usr/bin/haven-welcome` in `packaging/build-haven-welcome-deb.sh` | The POSIX shell launcher has no hard package dependency and only recommends `zenity` for `--gui`. No immutable package artifact or installed-package smoke is recorded here. |
| Ollama provider | `HUI/ollama-provider.json`; endpoint `http://localhost:11434` | Provider contract only. It requires an externally running Ollama service and model; no Ollama binary, package, or execution proof is supplied. |
| Write, Plan, Terminal, Wave, Images | No accepted source directory, package recipe, or executable entrypoint in this checkout | No authoritative implementation recovered. Branding assets and unrelated development commands are not applications. |

Canvas and Boards are outside this cohort and are intentionally not assessed or
modified here.

## Retired source-only builder

`packaging/cohort/build-debs.sh` and `verify-debs.sh` are retained only as
explicitly retired compatibility shims. They do not build, extract, or copy
Data, Present, Wine, or any other application source. The former source-carrier
flow copied Data and Wine source trees and a Present worker into `.deb` archives,
then checked only extraction and syntax. It did not install packages, launch an
application, or provide Data's DuckDB binding.

Use `release/package-preload-manifest.json` to admit only hash-pinned,
standalone Debian artifacts. `release/tests/validate-package-preload.ps1`
validates the metadata mapping without requiring `dpkg-deb`, `live-build`, or
package bytes.
