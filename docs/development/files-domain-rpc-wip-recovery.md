# Files domain RPC recovery checkpoint 05

This isolated code-only checkpoint is UNSELECTED, UNREVIEWED, UNCOMPILED and UNRUN. It is based on `31a7705d490b85bea09b59ef8984318fd3dcef79`; no global branch or installed package is updated.

## Publication refusal and retained write

The previous owner-await/installed-caller gap is addressed by requiring two genuine retained publication guards. The accepted private Home Session holds its original Context.Gate until the same physical reply write settles. Before it writes a Files result, it acquires an installed actor/process/receipt guard and a SAME owner-issued Home/Files read transaction, independently checks both, and retains both until the write and final checks settle. Every check and disposal failure remains observable with the original body/write failure.

No supplied current installation verifier exposes such a physical retained publication guard. No registered Files source currently implements the coupled transaction. Both are named prerequisites, and their default absence produces `Unavailable / FilesPublicationGuardUnavailable` before Files owner invocation or broker approval. The configured service remains unavailable without both supported ports. A successful old asynchronous verifier call, Core readiness, wire identifiers, a cached epoch or a moved await cannot supply these guards.

The new publication interfaces are trusted HOME-only extension points. They do not authenticate their own implementations. A selected implementation must retain actual canonical ownership and physical transactions; it must not reenter Context.Gate/Home/Files from a held guard. The actual profile/configuration/ownership lease, provider metadata lease and protected installed process/receipt transaction still need coherent implementation and full independent review. Therefore this checkpoint closes the exposed publication path conservatively; it does not claim usable installed Files authority.

## Preserved implementation

The same installed Core admission, continuing single pipe reader and client pending slot remain. Existing Core.Read operations and manual `permissions.trust` policy are unchanged. The read-only NativeHost handler retains genuine Files store binding, original task/page maps and canonical Home broker plans. It does not add mutation or package execution. The separate app surface borrows the original app connection and CUI readiness and publishes no raw Home objects across IPC.

The HOME-only staged helper remains default unconfigured and adds no store fallback. Parent checkpoint 03 added the owner, root-store read resolver, helper, remote surface and bounded paging observation. Parent checkpoint 02 retained the issuer-specific client/startup/app connection and exact A1 four-span composition. All original native route fixtures and the reviewed PR13 source remain unchanged.

## Ownership and validation

Home owns canonical actor/profile/permissions, accepted private Session, installed peer/lease, Core server and publication port. Files owns canonical read plan, provider/metadata and native consumer. The app provider and dispatcher initializer are distinct from the Home provider. Installed Windows package selection remains unavailable until real domain ports, canonical store, protected receipt authority and the executable entrypoint are supplied.

Relevant projects:
- `9to1 Workspace/Home/HavenOS.Home.csproj`
- `9to1 Workspace/Files/NativeHost/HavenOS.Files.NativeHost.csproj`
- `9to1 Workspace/Files/NativeUI/HavenOS.Files.NativeUI.csproj`
- `9to1 Workspace/shared/tests/Haven.Desktop.Tests/Haven.Desktop.Tests.csproj`

The full Windows Desktop build/test graph and existing seven native cases remain unrun. This checkpoint adds four authored Windows protocol methods/seven outcomes in `Home/Tests/HomeNativeFilesRpcPublicationTests.cs`: three missing-guard combinations, real canonical profile or controlled installed-tuple retirement while an owner check is held, actual frame-header/payload writer cancellation with guards and Session close held until finally settles, and exact independent two-check/two-close failures. The fixture uses actual Windows pipes/kernel credentials, canonical FileHome profile state and its physical publication lease. Its installed receipt and Files transaction are controlled fixture inputs, explicitly not protected installed or canonical Files metadata acceptance.

The owner now publishes and retains its exact source-acquisition and public raw current-check Tasks before callbacks. Owner close joins both Tasks independently even when reached outside the normal Session close path. The Windows server also captures the actual Files invocation Task and preserves a Files invocation/publication body cancellation before the legacy late-close transport waiver; context and server cancellation tokens are not presumed equal. The original broker/plan/current read paths remain unchanged.

Run the owning suite on the Windows receiver with:
```powershell
dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" --configuration Debug
dotnet test "9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj" --configuration Release
```
All four methods/seven outcomes are UNCOMPILED/UNRUN. No runtime, installed package, signing, protected receipt or global adoption acceptance is inferred from source publication.
