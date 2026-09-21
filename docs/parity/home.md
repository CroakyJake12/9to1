# Home Vertical Slice

## Scope

`9to1 Workspace/Home/` contains a platform-neutral Home domain and an authored `UI/Home.cui` surface. Its snapshot separates catalog, installed-app, update, settings, and runtime/model/voice sections.

## CUI Dependency

`HavenOS.Home.csproj` explicitly requires `framework/CUI/src/NineToOne.Cui.Markup.csproj` (or `CuiMarkupProjectPath`). `HomeCuiSurface` loads the authored `.cui` document and exposes typed `InstallAllUpdates` intent. It has no HUI dependency and does not manually construct a visual tree.

## Backend Status

No package-manager backend is present in this slice. `IHomePackageBackend` is the only package boundary. The default `UnavailableHomePackageBackend` does not perform installation, returns `Unavailable`, and the CUI displays the backend status while disabling Install all updates. Backend refresh and install exceptions become observable error states.

Settings and runtime/model/voice values are typed input snapshots. Their default state explicitly reports that a provider is not configured; no runtime, model, voice, or package state is invented.

## Verification

Focused tests cover unavailable backend behavior, backend refresh/install projection, failure reporting, all five authored CUI sections, and typed install intent. The CUI loader is verified, but a CUI renderer/host does not yet exist, so headed rendering, accessibility interaction, Linux/Windows launch, Android launcher behavior, and real package installation remain UNFINISHED.
