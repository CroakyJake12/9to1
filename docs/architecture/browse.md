# Browse architecture and acceptance status

The revised weekend directive makes Browse the main 9to1 host, with one canonical
tab/session model and one authored CUI shell. Host integration is in progress. Gecko is
the default engine; Chromium is an explicit alternative. Engine-local cookies,
profiles and renderer resources must remain physically isolated. An unavailable
Gecko adapter never authorises relabeling Chromium as Firefox.

## Implemented source boundaries

- `9to1 Workspace/Browse/Core/HavenOS.Apps.Browse.Core.csproj` compiles the maintained
  `BrowseChrome` state, engine selection and preference persistence once.
- `NativeUI/UI/BrowseWorkspace.cui` is the maintained native CUI surface. It consumes
  canonical tabs, bookmarks, history, permissions, download records and original
  engine views. The legacy `BrowseCuiScene` is not the current native entrypoint.
- `NativeWindows/WindowsChromiumEngineFactory.cs` provides a real Windows
  WebView2/Chromium adapter, including native navigation, download approval,
  permission, crash and retirement callbacks. Native execution is a separate gate.
- Engine selection preserves tab identity. Explicit tab overrides take precedence
  over destination-site rules, which take precedence over the default. Address,
  bookmark and history navigation resolve the destination before creating its
  renderer. Unavailable selected engines retain the destination and identity.
- “Use site or default engine” clears an explicit tab choice. Resetting a site
  preference preserves a genuine explicit tab choice. Standard engine preferences
  persist; private tab choices do not enter the standard preferences file.
- Bounded standard-tab reopen restores the same tab identity, position, URL and
  explicit engine preference through the original factory/profile contracts.
  Private tabs never enter the recovery list. Closed-tab history is currently
  memory-only. Session snapshot timestamps preserve stored tab order.
- Failed close persistence leaves selection on a real surviving tab. If the last
  tab's replacement engine fails, the original canonical tab remains recoverable.
- Private grants are scoped to the canonical origin and permission kind within
  their private tab. They cannot transfer across hosts, schemes or ports, and are
  not written into the standard permission store.
- Find and observed DOM zoom belong to each tab. Selection preserves each tab's
  value; a document navigation resets its DOM zoom. A failed page script cannot
  publish a successful zoom change. Engine-switch rollback retires a newly
  created renderer before recreating the previous engine.

- Built-in History, Bookmarks, Downloads and Settings are native CUI pages in the
  same canonical tab model. They restore standard identity and never create a
  renderer, web origin or browsing-history visit. Private native tabs are excluded
  from persistence/reopen. Exact route validation rejects aliases, credentials,
  ports, extra paths and query/fragment suffixes on these built-ins. Later
  tab persistence waits for previously accepted native commit writes.
- The typed host migration represents HTTP(S), Browse, generic CUI and native-app
  addresses separately with `AppContentAddress`. Literal `9to1://` values are not
  coerced into `System.Uri`; only HTTP(S) has a web origin or enters an engine.
  Malformed or reserved unknown input never falls through to external search.
- Native app factories return retained per-tab owners. Their actual initialization,
  close preparation, close and native view stay attached to that owner. The tab
  retains the exact authenticated Home profile identifier supplied by composition;
  stored identifiers are metadata, never authentication. Restore re-registers each
  resource with the current factory. Legacy Assistants routes migrate to
  `9to1://spaces/assistants/...`; the deprecated product is not a separate app.
- A separate explicit internal factory supplies `browse://home` and
  `browse://newtab`. A stable, nonclosable Home tab is an implementation default,
  not an owner-mandated behavior. Built-in history/settings pages keep their
  existing data and action paths. Generic `cui://` remains refused pending a
  separately scoped content owner.
- Native CUI actions queue immutable navigation intents against their source tab
  and return before the host-owned driver waits for the originating loader's
  actual action settlement. This avoids closing a surface from inside its own
  still-running action. Production app composition remains a separate
  shared-host acceptance gate.
- Ordinary Core and workspace close perform native unsaved-work preparation while
  the host's publication is still current. A rejected attempt leaves commands and
  the same native view usable for retry. Accepted retirement starts cancellation
  and actual owner close before joining held initialization. Both returned and
  reported tasks are retained on a contract mismatch. Notification failure cannot
  skip cleanup, and a failed native close keeps its view and scene mounted.
- `IBrowseTabSessionPersistence` projects basic tab identity, typed raw address,
  profile metadata and selected standard tab into the existing canonical workspace
  owner. The supplied adapter replaces tab-session load/save only; bookmarks,
  history and browser settings retain their original store. Groups, windows,
  layouts, pins and other rich state remain in `WorkspaceSessionSnapshot`, rather
  than being duplicated in a richer browser file. Exact load/save sources are
  retained, including selection saves and native page-commit session writes.

## Component verification

The last verified typed-host source snapshot passed **120/120 Core tests and 30/30 native CUI
tests in both Debug and Release**, with zero skipped. The focused tests cover
original owner/view identity, held initialization released by actual close,
unsaved-work veto and retry, publication-dependent preparation, retained close
failures, actual CUI queued navigation, Assistants alias restoration, and canonical
session projection/save tasks. These are deterministic component tests with
explicit engine/app/readiness fixtures, not installed native-app or renderer
qualification. The full shared-host composition is tested separately.

A subsequent source change adds the typed HTTP(S) guard to direct Core
Back/Forward/Reload/Stop calls before a navigation task is retained or original
work is cancelled. Ten new regression rows cover native/internal refusal without
state, ownership or close-lifetime changes, plus dispatch to the same original
HTTP(S) renderer. This change has only received source review and whitespace
validation: compilation and tests are deferred during the laptop power hold.
The component results above certify the earlier snapshot, not this guard change.

## Donor and runtime evidence

`Source/FirefoxGecko/` was restored on 16 October 2026 at the declared exact
Gecko donor pin `5836a062726f715fda621338a17b51aff30d0a8c`, with a registered
gitlink and clean donor working tree. `Source/DONOR-PROVENANCE.md`, `.gitmodules`
and `eng/donor-sources.json` retain the source provenance. Source retrieval does
not provide an installed or usable embedded engine.
There is no maintained Gecko factory, real Gecko page-render evidence or Gecko
build/update/rollback qualification in this checkout.

The standalone Windows executable has a CUI setup/recovery path. Its normal
Browse startup requires authentic installed Home/root/profile readiness and
services. Injected readiness in tests cannot supply that authority. The actual
installed launcher/bootstrap producer and Windows native execution remain open.

## Full acceptance remains incomplete

The current V2 source's Browse section requires all of the following. Tests of
existing Core/CUI paths do not establish completion of these families:

| Family | Current evidence / required work |
| --- | --- |
| Firefox donor parity and real dual-engine rendering | Pinned Gecko donor checkout restored; Gecko runtime supplier unavailable; Chromium source exists; real native qualification outstanding. |
| Validated upstream updates and rollback | Historical donor pins exist; complete dual-engine ingestion/promotion/rollback pipeline outstanding. |
| `cui://`, `browse://`, `9to1://`, HTTP(S) namespaces | Canonical native `browse://history`, `bookmarks`, `downloads` and `settings` tabs exist through exact route admission, original data/actions and CUI. Typed native-app owners and Home/NewTab composition seams have bounded Core/CUI component verification; actual shared-host composition is in progress. Generic `cui://` remains outstanding. |
| Tab-scoped profiles and mixed-profile workspaces | Standard/private isolation exists; full canonical multi-profile/workspace features outstanding. |
| Groups, Split View, four tab-strip orientations | Current tab strip exists; complete group/split/placement journeys outstanding. |
| New Tab canvas and Search/AI/dual-provider split search | Typed Home/NewTab routes and a shared-content factory seam exist; actual Apps/Dashboard/Models composition is in progress. Custom canvas and full search composition remain outstanding. |
| Dulche mentions and scoped autonomous browsing | Shared browser automation services exist; full Browse entity-mention and task-scope UI qualification outstanding. |
| Extensions, extension CUI pages and Site Enhancers | Full donor-compatible engine-aware implementation and permissioned journeys outstanding. |
| Download lifecycle and Files integration | Native approval transport and canonical completed-download/Files integration source exist; interrupted/retry UX and installed cross-app verification outstanding. |
| Recovery, privacy and platform acceptance | Owning deterministic regressions provide bounded evidence; real Gecko/Chromium crash recovery, installed Windows/Linux routes and applicable device/UI matrix remain Unvalidated. |

Browse retains its explicit browser-within-browser web exemption. The exemption
does not waive native donor functionality, Windows/Linux obligations, or any
applicable supported-platform acceptance. Never report full Browse, donor-complete
RCVP, package readiness or platform qualification from fixture-only tests.
