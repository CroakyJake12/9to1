# Windows host and publish

`HavenOS.Assistants.csproj` is the dedicated Windows executable. Its entry point
calls the public `Haven.Desktop.App.RunOriginalDesktop` bootstrap. The referenced
Desktop assembly receives the explicit `ProductInitialApp=assistants` declaration,
product display name and Windows App ID. It must register and open the dedicated
Assistants route through the original Home/provider/window lifetime.

The host does not copy Desktop startup, use reflection, create another provider,
infer current Home authority from a startup receipt or start a replacement Space.
The owning Desktop route and canonical runtime composition are receiving
dependencies. Until those execute successfully, this project is source for a
host, not a packageable or implemented product.

After the owning build slot and complete route are available, the supported
profile can be used from the Assistants directory:

```powershell
dotnet publish HavenOS.Assistants.csproj -p:PublishProfile=AssistantsWindows `
  --artifacts-path <isolated-artifacts-directory> -o <package-directory>
```

Use an isolated output for this product. Its referenced Desktop build carries
product metadata and cannot share an intermediate directory with a concurrent
Write, Present or general Desktop build. Preserve the exact source identity,
publish command/result, complete output manifest and installed Windows launch
evidence before advancing any readiness stage. No publish has been performed by
adding this profile.

Windows smoke must exercise the actual executable, original Home startup,
create/save/edit/reopen of the same Assistant, two retained conversations,
current-work observation and actual presentation close. Durable work must survive
view closure under its existing canonical owner. Configuration conflicts retain
the draft and never overwrite a newer revision. Missing resource/runtime owners
must produce an honest unavailable state. These checks do not replace the full
development, permission, proactivity, Mini Computer, multimodal, recovery and
appearance requirements in the current-pass instruction.

The browser requires its own registered route and actual browser Home/Den and
Conversation/Task owners. This Windows profile supplies no web evidence.
