# llama.cpp Debian packaging

`build-deb.sh` assembles a **model-free** `haven-llamacpp-runtime` package from the CakeOS runtime sources plus an already-built pinned `llama-server` executable.

The builder refuses an unverified-looking server unless `--version` contains both llama.cpp `0.4.0` and commit prefix `5266f24`. The upstream source itself is not copied into CakeOS; the package includes the required upstream MIT notice and the pinned upstream lock file.

The package contains no `postinst`, `prerm`, or other maintainer script. Installation therefore does not silently enable/start the user service. The model store is also absent; models remain user-owned data managed separately through explicit lifecycle actions.

Runtime dependencies reflect the CPU binary observed in PR #2: Python plus libc, libstdc++, libgomp and their normal loader/libgcc dependencies supplied by the Ubuntu base. The package is built in CI but **not installed** there or in the approved VM.

## CI evidence labels

A successful packaging job means:

- pinned llama.cpp source was built for the packaging job;
- a `.deb` was assembled;
- control metadata and absence of GGUF/model paths were checked;
- the package was extracted without installation;
- packaged Python was compiled and the packaged `llama-server --version` smoke test passed;
- package SHA-256 was emitted.

It does **not** mean the package was installed, the systemd user service started, a model loaded, inference succeeded, performance was benchmarked, or the approved VM was modified.
