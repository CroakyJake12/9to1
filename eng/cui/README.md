# Explicit canonical CUI source migration

This tool is for one-time, reviewed migration. Production parser/compiler/loaders never call it and must reject legacy application syntax themselves.

Run from the repository root:

```powershell
python -B eng/cui/migrate_legacy.py apps/Web/Home/Home.cui --report work/cui-proposal.json
python -B eng/cui/migrate_legacy.py apps/Web/Home/Home.cui --apply --report work/cui-applied.json
python -B eng/cui/migrate_legacy.py --inventory eng/cui/first-party-inventory.json
python -B -m unittest discover -s eng/cui -p test_migrate_legacy.py -v
```

Choose an existing task-owned output directory for reports. No source changes occur without `--apply`. The report retains before/after SHA256, exact path and conversion notes. Unknown custom controls, code-behind handlers and donor namespace routes require an explicit adapter; the tool does not invent Definitions or Object wrappers. Canonical shorthand that is not XML is preserved unchanged when no legacy tags occur. Mixed legacy/shorthand needing review refuses without writing the file. Raw C# string literals containing a full CUI document are supported without altering surrounding APIs/code.

The tool maps known layout and control semantics to canonical Container/Input/Button Types, moves donor grid placement/sizing to Row/Column/spans/RowSizes/ColumnSizes, converts star sizing to `fr`, preserves action declarations and binding payloads, and makes independent scrolling explicit. Existing element IDs stay attached to their elements. Page scope is added where an old document lacked it; a former Default theme scope becomes Home's `user_preference`.

`first-party-inventory.json` enumerates tracked and nonignored first-party markup with source hashes and owning project candidates. Vendor/third-party Source trees, release evidence and historical references are excluded. The two frozen Picture admission legacy resources remain historical control fixtures; they are not shipping authoring examples. `canonical-candidate` means source shape only: it does not claim compiled/rendered/functional acceptance.

The inventory deliberately retains failures for remaining AXAML/HUI application inputs and private/declaration-only app grammars. Every affected native entrypoint must be traced and migrated; an XML conversion or a retained metadata declaration is not an app. Home/Write/Web markup, all normal production CUI menus and the integrated native Files/Sites/Canvas seams use canonical public elements in this checkpoint. Native legacy HUI hosts and the unconnected Data/Mail/Spaces/MiniComputer/Terminal declaration paths remain explicit conformance work until their real typed CUI host/renderer contracts are implemented and exercised.

All seven APP-AC gates and donor/stage/platform acceptance remain separate. Refresh the inventory and run the actual strict compiler, current changed-graph tests, real app journeys and independent artifact review before claiming conformance.
