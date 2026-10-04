# Recorded Wave evidence — immutable fe3d candidate

Wave cold05: actual 14/14 PASS, exit0, zero recorded runtime errors. Real StorageFull refused a pending revision7, both durable stores remained identical at6; after reset the same edit saved once7. Independent B6 exact replay also reported14/14; its separate raw artifacts are not included here.

Calibration: hot quota03 really COMMITTED all3 native writes despite override1 (exit2); quota04 failed command-line metadata prerequisite before native writes (exit1,3NOT_RUN); cold quota05 genuinely aborted all3 native transactions with QuotaExceededError22, preserving both stores (exit0). Wave cold04 retained8PASS2FAIL4NOT_RUN: restored-page prerequisite refusal and trace-not-started finalization failure. No historical red is relabeled by cold05.

The native store bytes are only the closed Default/IndexedDB subtrees of fresh local Wave/probe profiles. They are not a complete browser profile. Raw traces and JSON preserve real synthetic canonical IDs/revision/content receipts. Never interpret local IDs/storage tokens as hosted Files/provider identity or revision. No account fixture identities/tokens are included. Cookies/history/auth profiles/cache are excluded.

Use transport-manifest.json originalPath→archivePath mappings to resolve retained absolute paths in byte-identical original receipts. `python3 verify_portable.py DIRECTORY` verifies every selected byte and exact directory denominator. Publisher manifest remains byte-identical; its inventory binds runtime/wwwroot. Actual owner source cuts are recovered via immutable commit git objects. See owner-source-custody.json for the original publisher omission; these later cuts do not become publisher before/after proof. Startup39 captured cuts remain published-provenance cuts even though the present working tree has changed.

Optional future coordinator-gated replay on a machine with matching Chromium151/a966 and Playwright (outside this recorded package):

```
B3_CANDIDATE_MANIFEST="$PWD/publisher/publish-manifest.json" B3_CANDIDATE_MANIFEST_SHA256=d985cbfc154ca295dc816ee7ff3e07a1676bb75b0cea54b3344393025d8852f8 B3_PORT=18761 PLAYWRIGHT_MODULE=/path/to/playwright node runners/wave-cold05/run-wave-browser-cold-quota.cjs runtime/wwwroot /tmp/FRESH-wave-replay
```

Use a fresh output/profile; do not reopen archived closed native stores for a new acceptance run. This command is documentation, not authorization/resource grant or a new execution. It requires the actual available original native browser/CUI/WASM runtime; no fake provider/API exception/audio replacement. Browser physical speaker audibility, actual provider/backend/cross-client Files acceptance, all Wave donor operations/formats, native admission/two-session cancellation and the remaining specialist app scope are separate. Deliberate clean blank navigation before restart does not prove automatic tab/URL restoration.
