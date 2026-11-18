# Browse and the native/browser tab host — Host; Every advertised app has a real activation path (part 03)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B2680 · v2 lines 14529–14529

## Host

### V2-B2681 · v2 lines 14531–14531

### UX27-TEST-058 — Every advertised app has a real activation path

### V2-B2682 · v2 lines 14533–14533

**Requirements:** UX27-HOST-001, UX27-SHARED-016 · **Status:** NOT RUN

### V2-B2683 · v2 lines 14535–14535

**Given:** Inventory of visible app entries with expected routed surface/build.

### V2-B2684 · v2 lines 14537–14539

**When:**
1. Launch each advertised in-scope app from Apps, direct route and standalone executable where specified.
2. Record source/build and instantiated owning surface.

### V2-B2685 · v2 lines 14541–14544

**Then:**
- No fake success or launch-only placeholder.
- Actual activated source matches the built candidate.
- A failing optional app is isolated and its missing dependency is actionable.

### V2-B2686 · v2 lines 14546–14546

### UX27-TEST-059 — Home and New Tab retain distinct identity

### V2-B2687 · v2 lines 14548–14548

**Requirements:** UX27-HOST-002, UX27-HOST-003 · **Status:** NOT RUN

### V2-B2688 · v2 lines 14550–14550

**Given:** Mixed native/web tab session with Home and New Tab.

### V2-B2689 · v2 lines 14552–14554

**When:**
1. Switch Apps/Dashboard/Models and New Tab views.
2. Create/select/close tabs and overflow a long tab row.

### V2-B2690 · v2 lines 14556–14559

**Then:**
- Correct view/selection is visible with readable selected-tab contrast.
- Home/New Tab do not become duplicate nested shells.
- Closing follows dirty-state protection and moves focus predictably.

### V2-B2691 · v2 lines 14561–14561

### UX27-TEST-060 — Chromium viewport and scrolling

### V2-B2692 · v2 lines 14563–14563

**Requirements:** UX27-HOST-004, UX27-HOST-005 · **Status:** NOT RUN

### V2-B2693 · v2 lines 14565–14565

**Given:** A supported real browser engine and test page with known geometry/scroll markers.

### V2-B2694 · v2 lines 14567–14569

**When:**
1. Resize and change display scale.
2. Use mouse wheel and trackpad in both axes; zoom and navigate.

### V2-B2695 · v2 lines 14571–14574

**Then:**
- Page is sharp and proportioned with accurate hit targets.
- Scroll direction and magnitude follow settings, with no double scaling.
- No security/CAPTCHA bypass is used as an alleged rendering fix.

### V2-B2696 · v2 lines 14576–14576

### UX27-TEST-061 — Missing engine does not disable native apps

### V2-B2697 · v2 lines 14578–14578

**Requirements:** UX27-HOST-006, UX27-HOST-009 · **Status:** NOT RUN

### V2-B2698 · v2 lines 14580–14580

**Given:** Browser engine deliberately unavailable in a disposable installation.

### V2-B2699 · v2 lines 14582–14583

**When:**
1. Open a web page, then a local Board and Present document.

### V2-B2700 · v2 lines 14585–14588

**Then:**
- Web failure names the unavailable engine and supported repair route.
- Native editors remain usable.
- No fabricated web screenshot or mandatory account connection hides the missing engine.

### V2-B2701 · v2 lines 14590–14590

### UX27-TEST-062 — Mixed session restore and failing target isolation

### V2-B2702 · v2 lines 14592–14592

**Requirements:** UX27-HOST-008, UX27-DATAFLOW-004 · **Status:** NOT RUN

### V2-B2703 · v2 lines 14594–14594

**Given:** Saved session with a board, deck, website and one missing local file.

### V2-B2704 · v2 lines 14596–14599

**When:**
1. Restart the host.
2. Reopen valid tabs and inspect the failed target.
3. Verify pending action logs.

### V2-B2705 · v2 lines 14601–14604

**Then:**
- Valid tabs restore stable identity and state.
- Missing file gets Locate/recovery rather than blocking the whole shell.
- Prior AI/Automation actions are not replayed just because tabs restore.
