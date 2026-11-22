# Stacks identities and managed project metadata

Stacks is part of Develop. It owns stack identities, lineage, effective-source composition, Root/Subroot/Freeze rules and managed reference metadata. Files and Home retain their project and permission authority. Selecting a stack alone does not grant access to its source.

Use the existing revisioned manifest and managed-reference implementation when changing a stack, resolving a conflict or computing an effective source. Check stale writers and an interrupted update. Reopen the project to confirm the same stack and root identities remain recoverable.

## Managed metadata

Files-backed projects store a versioned `stack.manifest.json` under `.branches/`, source materialisations under `.source/`, and Root metadata under `.roots/roots.json`. The local store rejects unknown schema versions and validates managed paths before opening a project. Writes use a same-directory temporary file followed by an atomic replacement.

Git-backed projects reserve `stack/twigs-and-leaves-dependency` for companion metadata. It is always treated as a protected managed ref, never as an ordinary user Branch. Provider integration is expressed through the typed interfaces in this assembly; a missing provider capability returns a structured `CapabilityUnavailable` failure.

The focused test project is `Tests/HavenOS.Stacks.Tests.csproj`.

[Engine](StackEngine.cs) · [Manifest](StackManifest.cs) · [Managed references](StackManagedRefs.cs) · [Tests](Tests)
