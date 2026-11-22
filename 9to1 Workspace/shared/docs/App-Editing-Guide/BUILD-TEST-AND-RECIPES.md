# Build and inspect a native change

1. In the canonical checkout inspect the current revision, relevant files and owners. Reproduce the action on the development copy. Record the route and current profile boundary.
2. Identify the smallest owning compiler, runtime, app or host test. Make one coherent edit and use the serial, guarded build procedure on the development PC. The root PowerShell orchestrator exposes component-specific entry points.
3. If the edit changes visible behaviour, publish the exact reviewed source, get an immutable candidate receipt, and reload the managed development copy through its normal lifecycle. A process that refuses to close may have pending work.
4. Revisit the original route using a pointer and the keyboard. For a preference, close and reopen with the same profile. Record the revision, platform and result.

Treat a missing dependency, a locked build output and a profile mismatch as separate failures. Inspect the first failed project or lifecycle receipt before retrying. Preserve failed logs and the last working candidate.

[Build entry](../../../../eng/9to1.ps1) · [Guarded workflow](../../../../docs/development/workflow.md) · [Candidate and reload procedure](../../../../docs/development/live-preview.md) · [Desktop solution](../../Haven.sln)
