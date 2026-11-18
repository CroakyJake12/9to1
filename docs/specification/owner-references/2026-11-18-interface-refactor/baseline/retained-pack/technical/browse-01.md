# Browse and the native/browser tab host — Apps; Workspace Apps (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B0375 · v2 lines 2084–2085

Apps
Workspace Apps

### V2-B0376 · v2 lines 2087–2088

<!-- UX27:BEGIN SECTION-04 -->
## 4. Haven/9to1 host, browser engines and application launching

### V2-B0377 · v2 lines 2090–2090

### UX27-HOST-001 — One real launch path per advertised surface

### V2-B0378 · v2 lines 2092–2092

**O/B.** Apps navigation, deep links, standalone executables and restored tabs resolve through the canonical application registry. Every listed installed app has a real registered surface and compatible services. Selecting Write, Boards, Present, Mesh or another app must not route to an unregistered placeholder or silently open an older implementation. Missing app/engine packages produce a scoped install/repair path with the requested target retained.

### V2-B0379 · v2 lines 2094–2094

A smoke matrix must open every in-scope advertised app from the actual Home Apps view, not merely instantiate classes in a test host. Record the route, resolved surface, package identity and observed result. One shared launch regression affecting many apps has priority over polishing a single successful scene.

### V2-B0380 · v2 lines 2096–2096

### UX27-HOST-002 — Home and New Tab identity

### V2-B0381 · v2 lines 2098–2098

**B/D.** The Home-icon tab exposes Apps, Dashboard and Models/Discover as distinct sidebar views. New Tab exposes those views plus New Tab. Preserve the selected destination and its filter/scroll state. Apps is an actual catalogue; Dashboard is a source-backed briefing; Models is discovery/configuration; none is a reskinned empty chat screen or a duplicate list pretending to be another product.

### V2-B0382 · v2 lines 2100–2100

Cards have real app icons, one primary name and status where relevant. Selecting an app opens its actual native surface in the intended tab. New-tab creation must not reset the browser profile or manufacture duplicate Home tabs unless requested. Search results distinguish installed, available-to-install and unavailable entries.

### V2-B0383 · v2 lines 2102–2102

### UX27-HOST-003 — Tab selection, closing and overflow

### V2-B0384 · v2 lines 2104–2104

**O/B/D.** Use the owner’s selected-tab light surface/black-text default unless an explicit accent override applies. Each tab has an icon, readable title, close affordance when appropriate and accessible state; overflow is navigable without shrinking every tab to an unusable glyph. Close hit targets must not overlap the neighbouring tab. Reordering previews the real insertion point and commits one organisational change.

### V2-B0385 · v2 lines 2106–2106

Closing an editor tab follows save/recovery semantics and is not permission to cancel an explicitly background-capable run. Middle-click behaviour follows the configured platform/product preference, with no accidental touchpad-region interpretation in application code. Reopen closed tab restores a real target and state, not a new empty document with the same label.

### V2-B0386 · v2 lines 2108–2108

### UX27-HOST-004 — Browser viewport and DPI correctness

### V2-B0387 · v2 lines 2110–2110

**O/B.** Allocate the embedded web view using actual content bounds and physical-pixel backing size appropriate to the current scale. Do not stretch a low-resolution bitmap to fit the window, double-scale CSS/content coordinates, or preserve a stale viewport after sidebar/window changes. Resize and monitor-DPI changes must update the engine’s viewport, rendering surface and input transform together.

### V2-B0388 · v2 lines 2112–2112

Test a known page with text, form fields and scrolling at several window sizes/scales. Check visual sharpness and that pointer hits match rendered targets. A screenshot alone cannot establish correct scroll/input transforms; native interaction evidence is required. Document the active engine/version and whether compositing or offscreen rendering is used.

### V2-B0389 · v2 lines 2114–2114

### UX27-HOST-005 — Wheel, trackpad and browser navigation

### V2-B0390 · v2 lines 2116–2116

**O/B.** Pass wheel deltas to the browser with the correct units, sign and scale exactly once. Small trackpad deltas accumulate smoothly instead of disappearing; a normal wheel notch must not move only an imperceptible amount because a second scaling factor was applied. Horizontal gestures, Ctrl+wheel zoom and ordinary two-finger scroll remain distinct. Back/forward and address changes use actual navigation entries, not simply swap titles.

### V2-B0391 · v2 lines 2118–2118

When a native app tab and web tab share the host, the active target receives input; a hidden web view cannot consume scrolling from Boards. Preserve scroll state for each tab independently. Test with an actual mouse and trackpad where available, and report the device-specific gap otherwise.

### V2-B0392 · v2 lines 2120–2120

### UX27-HOST-006 — Engine switching and failure states

### V2-B0393 · v2 lines 2122–2122

**B/D.** Firefox/Gecko and Chromium/Blink remain separately identified adapters with isolated engine-private profiles. Changing an engine keeps the canonical tab/workspace target while recreating the supported web session; do not promise transfer of live DOM/JavaScript state. Persisted site preferences are inspectable and reversible. When one engine is missing or fails, offer the available authorised alternative or repair path without pretending that a renderer is connected.

### V2-B0394 · v2 lines 2124–2124

A missing Chromium message must not disable local Boards, Write or Present. Preserve the pending URL and user-visible cause. Site authentication/verification failures require compatibility investigation using legitimate browser behaviour; do not bypass CAPTCHA, impersonate security state or weaken site protections to claim success.

### V2-B0395 · v2 lines 2126–2126

### UX27-HOST-007 — Profile/account popup lifecycle

### V2-B0396 · v2 lines 2128–2128

**O/B.** Profile/account menus belong to the current host anchor and follow the shared popup contract. Outside click, Escape and navigation that invalidates the context dismiss them. Switching account/profile shows the actual resulting identity before a consequential operation. A popup left over a different app is a failure even if its buttons still work.

### V2-B0397 · v2 lines 2130–2130

Keep browser profile, 9to1 account and document ownership distinguishable. Changing a browser profile is not permission to copy cookies, credentials or private document context into another profile. Local document availability must not be represented as dependent on an unrelated cloud sign-in.

### V2-B0398 · v2 lines 2132–2132

### UX27-HOST-008 — Session restoration and stale artifacts

### V2-B0399 · v2 lines 2134–2134

**B/D.** Restore typed target identities, current locations and permitted view state. Revalidate current package/provider availability and access. Never persist a short-lived in-process object pointer or one-shot capability as the only reopen data. Show a recoverable unavailable-target card for a missing item, leaving other tabs usable.

### V2-B0400 · v2 lines 2136–2136

A restored board must reopen its saved file/draft and actual page. Reloading the host must not replay AI prompts, external actions, saved graph executions or old permission responses. Test a mixed session with native apps and web pages, restart, and verify each tab independently.

### V2-B0401 · v2 lines 2138–2138

### UX27-HOST-009 — Startup and dependency repair

### V2-B0402 · v2 lines 2140–2140

**B/D.** Render the selected theme immediately and expose the real shell before slow document-dependent regions where safe. Instrument cold launch, warm launch, route activation, dependency discovery and first input separately. A brief truthful loading state is preferable to an unexplained white flash or a hidden window that never becomes interactive.

### V2-B0403 · v2 lines 2142–2143

A failed optional dependency is scoped; a missing required local engine names the package/architecture and supported repair action. Never ask the user to connect accounts to compensate for a missing native drawing library. Installation, repair and restart remain permission-controlled and preserve open work.
<!-- UX27:END SECTION-04 -->
