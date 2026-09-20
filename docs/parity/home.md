# Home Vertical Slice

## Scope

`9to1 Workspace/Home/` now contains a small, platform-neutral Home domain and a real Haven.UI CUI scene. Its snapshot separates catalog, installed-app, update, settings, and runtime/model/voice sections.

## CUI Dependency

`HavenOS.Home.csproj` explicitly requires `9to1 OS/HUI/vendor/Haven.UI/Haven.UI.csproj` (or `HavenUiProjectPath`). The scene is CUI-ready and uses Haven.UI primitives directly; it is not registered in a desktop or Android shell because those integration points are outside this ownership boundary.

## Backend Status

No package-manager backend is present in this slice. `IHomePackageBackend` is the only package boundary. The default `UnavailableHomePackageBackend` does not perform installation, returns `Unavailable`, and the CUI displays the backend status while disabling Install all updates. Backend refresh and install exceptions become observable error states.

Settings and runtime/model/voice values are typed input snapshots. Their default state explicitly reports that a provider is not configured; no runtime, model, voice, or package state is invented.

## Verification

Focused tests cover unavailable backend behavior, backend refresh/install projection, failure reporting, all five CUI sections, and keyboard invocation of the typed install intent. Host registration, headed CUI rendering, Android launcher behavior, and a real package installation remain unvalidated because no package backend or Home host route is in this owned slice.
