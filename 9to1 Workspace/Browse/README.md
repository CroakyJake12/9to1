# Browse

Browse owns canonical tab/session, engine preferences, bookmarks, history,
permissions, private profiles, popup policy, recovery and completed-download state.
`Core/HavenOS.Apps.Browse.Core.csproj` compiles the maintained domain source once.
`NativeUI/UI/BrowseWorkspace.cui` is the maintained canonical CUI shell. The legacy
Haven.UI scene remains in the compatibility app assembly through type forwarding.

The native shell binds real operations to `BrowseChrome`, including tab selection,
new/private/close, address/bookmark/history navigation, back/forward/reload/stop,
engine preferences, popup permissions, find/zoom, recovery, developer tools and
completed-download Files integration. Standard closed tabs can be reopened through
the authored control or Ctrl+Shift+T; Ctrl+Shift+P opens a private tab. Up to 25
closed standard tabs retain their canonical identity, placement and explicit
engine preference in memory. Private tabs never enter this recovery list; this
is not a persistent closed-tab history implementation. Current source-issued row targets guard
against stale or foreign selections. Native tab views remain mounted while
inactive, preserving their actual renderer state. Authored buttons reflect the
same action availability enforced by command admission; editable inputs reflect
workspace readiness/busy state.

Engine selection retains tab identity. Explicit tab choices override site rules;
site rules override the default. Navigation resolves the destination rule before
creating its renderer. An unavailable selected engine retains the destination and
offers explicit engine choices. “Use site or default engine” removes a tab
exception. Resetting a site rule preserves explicit tab choices. Engine-switch
rollback retires a new renderer before recreating the previous backend.

Private grants are keyed by canonical origin and permission kind within their
private tab, never written to the standard permission store. Canceled permission
or tab operations do not perform their initial mutation. Find and confirmed DOM
zoom are tracked per tab. Zoom resets on an actual new committed document;
requesting navigation alone is not evidence that the document changed. Session
snapshots use one timestamp so the canonical tab order survives shared-store
sorting and restart. A failed close retains valid selected state; a failed last-tab
replacement exposes recovery for the same retired tab. Failed
scripts cannot publish successful zoom or find state. Native committed-page
receipts, rather than requested addresses, supply browsing history.

Built-in `browse://history`, `browse://bookmarks`, `browse://downloads` and
`browse://settings` are real native CUI tabs through the canonical model, shared
store and existing saved-entry/download/settings actions. They never load in a
web renderer or enter web history. Native pages restore standard tab identity;
private native tabs never persist or reopen. The exact allowlist rejects origin
spoofing on built-in routes. The unified-host source now carries typed
`AppContentAddress` values and retained native-app owners in the same tabs.
`9to1://` never becomes a fake URI or a web search. Native owners revalidate
canonical app resources and the original Home profile on initialization/restore;
serialized profile metadata cannot authorize access. Deprecated Assistants routes
canonicalize to Spaces. A separate Browse-owned factory supplies stable Home and
NewTab tabs. Generic `cui://` remains refused pending its own scoped owner.
Actual shared-host composition and the complete revised architecture still need
acceptance beyond these source seams and deterministic lifecycle tests.

Native close preparation keeps host publication current until retirement is
accepted. A veto leaves commands and the same view alive for retry. Accepted
retirement starts actual owner close before joining held initialization; failed
close retains the scene. An optional `IBrowseTabSessionPersistence` projects
standard tabs and selection into the existing canonical workspace session owner,
without duplicating its group/window/layout/pin schema or moving browser data.

## Platform implementation and outstanding gates

`NativeWindows` contains an actual Chromium/Blink adapter using the maintained
Avalonia WebView and WebView2 packages. It consumes original native navigation,
security, permission, crash and download callbacks; downloads use the existing
approval owner and transport. Native retirement retains original asynchronous
sources and actual adapter-destruction evidence.

Firefox/Gecko remains the default, but no usable Gecko runtime supplier exists in
this checkout. The declared `Source/FirefoxGecko/` donor and tracked gitlink were
restored at exact pin `5836a062726f715fda621338a17b51aff30d0a8c`; see
`Source/DONOR-PROVENANCE.md`. This source retrieval supplies no engine runtime.
Chromium is never relabeled as Firefox, and an external browser or HTML
stub does not replace the required embedded engine.

`9to1.Browse` is a standalone executable with an authored CUI setup/recovery route.
Normal Browse activation still requires an authentic installed Home/root/profile
startup attempt. Test readiness and constructor injection do not supply installed
authority. Actual installed startup, Gecko, full namespace routing, multi-profile
workspaces, groups/splits/orientations, New Tab canvas, full Dulche integration,
WebExtensions, Site Enhancers and engine update/rollback qualification remain open.
See [the architecture and acceptance matrix](../../docs/architecture/browse.md).

## Owning checks

The last verified typed-host component snapshot passes **120 Core tests and 30 native
CUI tests in each of Debug and Release**, with zero skipped. This includes native
ownership, real CUI action queuing, unsaved-work retry, close ordering, alias
restore and canonical session projection/save-source regressions. The previous
Windows component evidence predates this migration and needs a separate rerun.
A later source change guards direct Core Back/Forward/Reload/Stop calls before
retaining navigation work, matching the UI's HTTP(S) restriction. Ten added
regression rows are awaiting compilation and execution while the laptop power
hold remains active; the counts above apply to the earlier verified snapshot.


Run from the repository root, serializing builds with the other applications:

```sh
dotnet test "9to1 Workspace/Browse/Tests/HavenOS.Apps.Browse.Tests.csproj" -c Debug -m:1 --disable-build-servers -p:UseSharedCompilation=false
dotnet test "9to1 Workspace/Browse/Tests/HavenOS.Apps.Browse.Tests.csproj" -c Release -m:1 --disable-build-servers -p:UseSharedCompilation=false
dotnet test "9to1 Workspace/Browse/NativeUI/Tests/HavenOS.Apps.Browse.NativeUI.Tests.csproj" -c Debug -m:1 --disable-build-servers -p:UseSharedCompilation=false
dotnet test "9to1 Workspace/Browse/NativeUI/Tests/HavenOS.Apps.Browse.NativeUI.Tests.csproj" -c Release -m:1 --disable-build-servers -p:UseSharedCompilation=false
dotnet test "9to1 Workspace/Browse/NativeWindows/Tests/HavenOS.Apps.Browse.NativeWindows.Tests.csproj" -c Debug -m:1 --disable-build-servers -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true
```

Core tests use explicit deterministic engine fixtures. NativeUI tests use the real
CUI loader, authored actions, original-source lifetime and headless rendering,
with explicit engine/readiness/Files doubles. Optional
`BROWSE_EVIDENCE_DIRECTORY` records rendered desktop/narrow screenshots for all
four appearances. None of this certifies a real Gecko/WebView renderer, an
installed Home authority, Windows/Linux device behavior, or full Browse acceptance.
