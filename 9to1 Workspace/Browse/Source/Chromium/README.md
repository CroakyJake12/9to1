# Chromium donor source

`src/` is the actual Chromium repository pinned at `e23cdf4ab7b386d7e5fc58506b2d77f3f4a4817d`, tree `61f2a6863d0fe31d533421aca69a7937a13eed2b`. This was an observed main-branch commit on 9 October 2026, not a verified stable browser release. Canonical source: https://chromium.googlesource.com/chromium/src . Official mirror: https://github.com/chromium/chromium . Retain all original licenses and notices.

The acquisition job checks out this entire top-level repository, verifies its committed files and native browser-service sources, and preserves a source archive in parts smaller than the connector's artifact size limit. It does **not** claim that Chromium's separate DEPS repositories, build tools, binaries, or CakeUI host have been installed.

From the parent 9to1 checkout, acquire the pinned source with:

```sh
git submodule update --init --checkout --depth 1 -- "9to1 Workspace/Browse/Source/Chromium/src"
python3 "9to1 Workspace/Browse/Source/verify_chromium_source.py"
```

A build-ready checkout additionally requires Chromium's official depot_tools workflow and sufficient storage. With depot_tools installed, run from this `Chromium/` directory:

```sh
gclient sync --nohooks --no-history --revision src@e23cdf4ab7b386d7e5fc58506b2d77f3f4a4817d
```

The acquisition job has not run that dependency sync or Chromium's build hooks. Follow the donor's pinned `src/docs/linux/build_instructions.md` or appropriate platform instructions for those separate steps. Do not reset or overwrite an existing working source checkout.

This import does not change Browse's default Gecko selection. Both donor engines must retain their real native functionality when wired into CakeUI; source presence alone is not implementation parity.
