# Browse Firefox/Gecko donor source

The real Git submodule at `FirefoxGecko/` is pinned to Mozilla Firefox release-branch commit `3c71aeacdb296402e41fb2084b13e797d1770642`, source tree `97cd3d3e9a2f94733f74f07a2c8f857f64950088`, observed 9 October 2026. The maintained upstream is https://github.com/mozilla-firefox/firefox . The former gecko-dev mirror is retired; the old provenance note claimed an import that was absent from the parent Git tree.

This imports the full Firefox donor, including Gecko and native browser services. Preserve the donor's LICENSE and notices. No donor code is replaced, patched, or relicensed by this source-acquisition change. Bookmarks, history and downloads must continue to use the donor implementations when the CakeUI host is connected.

From the 9to1 repository root:

```sh
git submodule update --init --checkout --depth 1 -- "9to1 Workspace/Browse/Source/FirefoxGecko"
python3 "9to1 Workspace/Browse/Source/verify_firefox_source.py" --evidence firefox-source-evidence.json
```

Do not use `--remote`: the committed gitlink, not a moving branch, is the reproducibility pin. Do not force/reset an existing donor checkout with uncommitted work. The verifier requires all tracked files, the expected commit/tree, native service files, and an unmodified non-sparse donor checkout. Its regression tests use explicitly synthetic fixtures and are not browser runtime tests.

The `Browse - acquire and verify real Firefox donor` Actions workflow downloads the full pinned source, checks it, and retains a source archive plus SHA-256 and verification evidence for 14 days. Inspect the actual run before claiming that its checkout or artifact upload succeeded.

**Source acquisition is not a compiled Gecko host or CakeUI integration.** Native build, host registration, rendering, donor-service routing and runtime tests remain separate implementation work; `docs/architecture/browse.md` records the host boundary.

Shared progress and user messages: https://docs.google.com/document/d/1tDRauJ9_2xr0tE-8oaCOKXblheMVf8pyNBnp6c3KHgY/edit . Read this log and the current branch before resuming; append evidence and decisions without replacing user messages. The V2 product specification remains authoritative.
