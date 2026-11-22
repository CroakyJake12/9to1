# Review and migrate a legacy CUI document

Run the migration tool against an owned source path and inspect the proposal before applying it. Its report retains the path, before/after hashes and semantic conversions. Confirm that the actual actions, bindings, layout, overflow and object identities remain intact in the result.

The migration covers known controls and container forms. Unknown custom or donor controls need a reviewed adapter. Mixed shorthand requiring manual review should retain the original bytes. Keep the normal parser and production loader independent of the migration utility.

The command examples use a task-owned report location. Inspect a dry-run report and the source diff, then compile and exercise the owning app through the project’s normal guarded workflow.

```powershell
python -B eng/cui/migrate_legacy.py apps/Web/Home/Home.cui --report work/cui-proposal.json
python -B eng/cui/migrate_legacy.py apps/Web/Home/Home.cui --apply --report work/cui-applied.json
python -B eng/cui/migrate_legacy.py --inventory eng/cui/first-party-inventory.json
python -B -m unittest discover -s eng/cui -p test_migrate_legacy.py -v
```

## What to review in the conversion

Inspect canonical container/input types, Row/Column positioning, spans, fractional grid sizes and independent scroll axes. Keep element IDs, action declarations and binding payloads stable. The utility also recognizes a complete CUI document inside a raw C# string while preserving the surrounding API. Do not infer that a converted source has run until its owning host loads it and accepts the same normal input.

[Migration utility](migrate_legacy.py) · [Producer tests](test_migrate_legacy.py) · [Guarded validation](../../docs/development/workflow.md)
