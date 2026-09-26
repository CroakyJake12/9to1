# 9to1 Stacks

Stacks is the project-local source-control and collaboration domain. The implementation in this directory owns Stack identities, domain lineage, effective-source composition, Root/Subroot/Freeze rules, conflict state, managed metadata and provider contracts. Shared shell, Files, identity, credentials, Dev and Home permissions remain owned by their existing products.

## Managed metadata

Files-backed projects store a versioned `stack.manifest.json` under `.branches/`, source materialisations under `.source/`, and Root metadata under `.roots/roots.json`. The local store rejects unknown schema versions and validates managed paths before opening a project. Writes use a same-directory temporary file followed by an atomic replacement.

Git-backed projects reserve `stack/twigs-and-leaves-dependency` for companion metadata. It is always treated as a protected managed ref, never as an ordinary user Branch. Provider integration is expressed through the typed interfaces in this assembly; a missing provider capability returns a structured `CapabilityUnavailable` failure.

The focused test project is `Tests/HavenOS.Stacks.Tests.csproj`.
