# Browse Architecture

## Intended engine-neutral model

Browse owns one CUI shell: tab strip, address/search controls, navigation,
history, bookmarks, downloads, permissions, profiles, recovery, and settings.
It must not have separate product chrome for Gecko and Chromium.

Each tab must carry its resolved engine identity. The global default is Gecko;
site and tab overrides are explicit settings. Standard and private profiles
must be isolated per engine and private data must never be restored or written
to the standard profile. Chromium is optional and may be selected only when an
available adapter and user policy permit it.

```text
Browse CUI shell
  -> engine selection (global default, site override, tab override)
  -> Gecko adapter (default) | Chromium adapter (optional)
  -> isolated native profile + tab renderer
```

## Current evidence and gap

`HavenOS.Apps.Browse` already owns browser state, privacy, permission, popup,
download, recovery, and an engine-host seam (`IBrowseEngineHostFactory`). It
does **not** currently expose a Gecko/Chromium identity, select an engine per
tab, or register an engine factory. Pinned Firefox/Gecko source and MPL-2.0
provenance are now under `9to1 Workspace/Browse/Source/FirefoxGecko/` and
`Source/DONOR-PROVENANCE.md`; there is still no Gecko adapter/build/package proof.
The existing `BrowseCuiScene` still builds legacy HUI controls, so it is not the
required CUI shell.

WebView2 exists in legacy Windows desktop and file-preview code, but is neither
evidence of Gecko support nor the defining Browse architecture.

## Promotion gates

Do not call Firefox-first Browse implemented until all of these are evidenced:

1. A licensed, pinned Gecko integration and build/rebase provenance are present.
2. The engine selection policy defaults to Gecko and persists global/site/tab
   overrides without leaking profile data between engines or private tabs.
3. The one CUI shell renders a real Gecko page, creates/changes tabs, navigates,
   handles history/downloads/permissions, survives a renderer crash, and reports
   the exact engine for each tab.
4. Optional Chromium follows the same shell and safety policy, with no
   Chromium-only product paths.
5. Linux and Windows build/launch/presentation evidence is captured.
