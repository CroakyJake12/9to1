The browser foundation is source-backed and locally executable at
`83c24b2f0033fb2828169921fc905a327120143b`. The frozen published output is
`/workspace/team-b-artifacts/browser-83c24b2/wwwroot` (180 files, 35,339,902 bytes).
No shared/native source or browser runtime module was substituted.

Actual Chromium 151.0.7922.173 verification passed 14 local checks: bootstrap,
disabled Studio and enabled Library pointer behavior, typed live/cold routes,
invalid links, and history. B6 independently replayed the unchanged pointer
runner, passing 5 checks including visible Library/Events heads. Managed runtime
errors, page errors, and failed requests were zero. The incidental favicon 404
and ten local WebGL warnings are recorded, not suppressed.

Full Home and browser parity remain blocked. Dashboard content visibly overlaps
because the unchanged owner conditional branch uses an overlay panel for several
direct siblings. The browser accessibility tree exposes status and main only.
Native persisted BFCache restoration was not observed; authenticated private
restoration and real service operations remain untested. This local artifact is
not a deployed or accepted release.

Earlier native-input, runtime-exit, and pointer failures remain distinct evidence.
The CSS-mutated diagnostic page is not shipped-source acceptance. The overflowing
runtime-exit trace is retained locally; its inflated raw contents should not be
included in the handoff package.

Reproduce only after reserving the local browser/port resource:

```bash
/opt/codex/runtimes/codex-primary-runtime/dependencies/node/bin/node \
  /workspace/team-b-evidence/sol-happy-20261003/b1/production-host/run-production-host.cjs \
  /workspace/team-b-artifacts/browser-83c24b2/wwwroot \
  /workspace/team-b-evidence/sol-happy-20261003/b1/production-host/replay-83c24b2
```

The runner serves the actual published directory read-only on port 18763 and
closes its browser/server. The JSON manifest records source/artifact hashes,
executed runner identity, logs, screenshots, trace, scope, and owner blockers.
