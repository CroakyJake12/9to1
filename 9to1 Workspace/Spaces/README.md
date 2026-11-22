# Spaces, conversations and task context

Spaces belongs to Coordinate and owns conversation membership. Its registry records durable Space identity. A selected Space, Assistant or Specialist may supply context for Chat; selecting it does not start a Task/Run or grant execution permission.

The task workspace uses the existing conversation and coordinator services. A development project reference records the originating workspace, project, root and applicable repository binding. Recheck permissions and the current revision when an operation goes beyond reading that reference.

The native Chat controls, typed controller and test suite live together in this workspace. Exercise switching conversations, retaining a draft, invoking a genuine action, and returning to the same Space. Temporary remains a Chat toggle and response choices belong in Options.

```powershell
dotnet test "9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj" -c Release --filter "FullyQualifiedName~SpaceDevelopmentWorkspaceTests"
```

[Chat UI](UI/Chat/ChatSpace.cui) · [Chat controller](Source/Chat/ChatSpaceController.cs) · [Development references](Source/Development) · [Task workspace](Source/Tasks) · [Spaces requirements](../../docs/specification/owner-references/2026-11-22-atlas-v4/README.md)
