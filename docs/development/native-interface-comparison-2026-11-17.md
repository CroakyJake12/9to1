# Native interface comparison pass — 17 November 2026 (owner request)

Owner asked to use the running apps and compare interaction affordances
against reference products: Spaces↔ChatGPT, Boards↔AppFlowy,
Haven/Browse↔Firefox, Files↔Google Drive (right-click parity, working
buttons, consistent CUI theme).

## Root cause found and fixed tonight

**No flyout, dropdown or context menu rendered anywhere on this
GNOME/X11 session.** Proven by direct interaction: right-click on a
Haven tab → nothing (Firefox shows new-tab/close menus); right-click a
Files row → row selects, no menu (Google Drive shows its full context
menu); New/edit and the sort ComboBox in Files → focus ring, no popup;
hover tooltips appeared but at the wrong offset — the signature of
Avalonia's native popup windows being suppressed/misplaced by this WM,
while the tooltip path used a different host.

Fix: `X11PlatformOptions.OverlayPopups = true` in `Program.cs`
(commit `780df2197`) — render popups in-window, which the vendored fork
supports. Verified live after the rebuild: the Browse settings flyout
and the Browse context menu (Web Automations / Web Tools ▸ / Browse
Settings / Close menu) now open at the pointer. Remaining menu gaps are
therefore **feature gaps, not infrastructure gaps** — each surface
simply hasn't populated its menu items yet.

## Confirmed working tonight (on-screen)

- Files layout after the owner's corrections: no Files-internal tabs,
  flat Locations (Files, Trash, Google Drive, Server Drive (R2, S3),
  Self-Hosted Drive (WebDAV)), folder list with columns, double-click
  opens folders, back/up/refresh, selection, "8 items" status.
- Drive rows are one click from consent (browser launch from the app
  still blocked by snap/DBus — consent link handed to the owner; app
  fix queued to open it as a Haven Browse tab instead of xdg-open).

## Feature gaps to close (comparison queue, not yet done)

| Surface | Reference behaviour missing in 9to1 |
| --- | --- |
| Haven tabs | Right-click menu: New tab to right, Close, Close others, Pin, Duplicate (Firefox) |
| Browse address bar | Right-click: Cut/Copy/Paste/Select all + search-engine entry (Firefox) |
| Files rows | Right-click: Open, Preview, Rename, Move to, Copy link, Download, Trash, Details (Drive); F2 rename, Delete key, Ctrl+A multiselect |
| Files empty area | Right-click: New folder / Upload / Sort by / View (Drive) |
| Files theming | Icon column uses one flat folder glyph (yellow) instead of the themed set; sort combo label clips ("Name: A–Z (pa") |
| Spaces | Conversation row hover ⋯ / right-click menu (ChatGPT: Rename, Pin, Delete); composer send button enable-state styling |
| Boards | Block right-click menu (AppFlowy: turn into, duplicate, move, delete) — was not yet exercised this session |
| Popup polish | Tab hover tooltip renders detached from the anchor (separate from overlay fix) |

Every row above is now implementable against a working popup
infrastructure; before tonight none of them could appear at all.

## Post-fix progress (same night, owner de-jank directive)

Working on-screen after the overlay-popup fix (`780df2197`):

- Browse context menu (Web Automations / Web Tools / Browse Settings)
  and the tab-strip browser menu render at the pointer — first time
  ever (`ux-fix-filerow.png`).
- The full Files command menu renders with correct enabled/dimmed
  states per the canonical command gate (`ux-ctx-newedit.png`).
- Drive OAuth now tolerates secret-bearing console clients (optional
  0600 secret file), always leaves the consent link at
  `/tmp/9to1-drive-auth-url.txt`, and reports honestly when a
  sandboxed session blocks launching a browser from inside the app
  (`45ddd44a`). Row-attached context menu shipped (`a9dfba550`).

Remaining defect proven tonight: right-clicking a Files **row**
selects it (and shows details) but never raises ContextRequested —
SUPERSEDED minutes later: with the menu attached to the generated
container (run `db9f6dd5`), the owner confirmed **right-click on a
row opens the menu on the real mouse**. The failing verdicts produced
by synthetic XTEST input were an artefact — on this display
(Xwayland) synthetic pointer *releases* never arrive at all (proven
with a minimal standalone repro: left and right releases both lost,
with and without overlay popups), while Avalonia raises
ContextRequested on release. Real input works; synthetic-input
conclusions about menus are void. Open menu items on the list are now
just the per-surface feature work above, plus the Haven-tab right-click
needing the same owner eyeball.

One instance (slot `ctx2-haven-1117`, run `42d53b65`) exited
unprompted shortly after startup (~22:50 UTC) with no severity≥3 log
line recorded — successor build runs stable (`50fc8c5c`); crash hunt
queued with the input-trigger item.

Evidence: `ux-*.png` captures in the PC session evidence directory
(before/after pairs for tab right-click, Files row right-click,
New/edit, sort combo, and the first menus that ever rendered:
`ux-fix-sort.png`, `ux-fix-filerow.png`).
