# Publish the Windows Assistants compatibility entry

The Windows host enters the original Desktop bootstrap with the Assistants initial-route declaration. Home, the active provider and the existing native window lifetime determine whether the route can open. Do not create a replacement profile or a new Space when resolving the definition.

The publish command below builds the existing compatibility executable. Inspect the emitted identity and launch the real host on Windows before claiming that it presents the expected resource. A successful publish alone does not establish current profile authority or session routing.

```powershell
dotnet publish HavenOS.Assistants.csproj -p:PublishProfile=AssistantsWindows `
  --artifacts-path <isolated-artifacts-directory> -o <package-directory>
```

[Windows entry](../HavenOS.Assistants.csproj) · [Resource ownership](../README.md) · [Build custody](../../../docs/development/workflow.md)
