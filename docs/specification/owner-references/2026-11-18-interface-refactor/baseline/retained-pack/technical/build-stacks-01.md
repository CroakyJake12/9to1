# Build Stacks — Branch, Twig and Leaf source control — Stacks; Product role (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1708 · v2 lines 9043–9045

Stacks
Product role
Stack is 9to1's team-first source-control and software-collaboration product. It is not a conventional GitHub clone or a desktop Git client. It combines Git hosting/collaboration with a higher-level project model designed for parallel team development, cascading shared frameworks, strong source-domain rules, public collaboration and deep integration with the rest of 9to1.

### V2-B1709 · v2 lines 9048–9049

Donors
Primary donor: Gitea. Preserve mature donor features, quality-of-life behaviour and collaboration capabilities wherever they remain applicable, including repositories, users/teams, permissions, history/diffs, reviews, issues, releases, Git LFS, migration/mirroring and related backend features. Stack-specific behaviour replaces donor behaviour where the models conflict.

### V2-B1710 · v2 lines 9052–9052

Secondary donor/reference: Gitnuro may be used for local desktop Git workflows and UX patterns where useful.

### V2-B1711 · v2 lines 9055–9055

Donor parity must be explicitly audited. Every mature donor feature must be preserved, replaced by a 9to1-native equivalent, or explicitly recorded as not applicable.

### V2-B1712 · v2 lines 9058–9062

Project and storage model
Each Stack project is an independent project boundary. A Stack project may use:
* 9to1 Files as the source backing store;
* its own dedicated public or private GitHub repository;
* both Files and GitHub together, according to the user's configuration.

### V2-B1713 · v2 lines 9065–9065

Stack must never combine unrelated Stack projects into one shared repository.

### V2-B1714 · v2 lines 9068–9068

Public Stack projects MUST expose a dedicated user-facing source-browsing, forking and contribution interface. Normal participation MUST NOT require direct access to Stack-managed backing folders, manifests or dependency branches.

### V2-B1715 · v2 lines 9073–9073

Stack supports mixed public/private project components through an explicit visibility model attached to canonical resources/domains. Public projection is generated only from resources whose effective visibility permits publication; private files, objects, commits, credentials, metadata and historical objects MUST be excluded from the public Git/source projection, not merely hidden in the UI. Before publishing or mirroring a mixed-visibility project, Stack MUST validate the projected commit/object graph and block publication if private ancestry/content would be reachable.

### V2-B1716 · v2 lines 9078–9081

Startup and app shell
Stack has two startup modes:
* New Session: open Home.
* Last State: restore the previous tabs and navigation state.

### V2-B1717 · v2 lines 9084–9084

If Last State would reopen a tab/state that is known to have caused a repeatable crash, Stack must suppress or close the offending tab rather than crash-loop.

### V2-B1718 · v2 lines 9087–9089

Stack always uses a tabbed interface:
←  →  |  🏠  |  Project Tab 1  |  Project Tab 2  |  +
Home is permanently available as the leftmost tab. Opening a project opens a new tab. The + button opens a new tab.

### V2-B1719 · v2 lines 9092–9092

Home contains the main dashboard and its own sidebar pages, including My Projects, Discover Projects, Audit Log, My Team, Settings and similar administration/discovery surfaces. These pages stay inside Home rather than becoming separate top-level app tabs.

### V2-B1720 · v2 lines 9095–9095

Clicking Home while Home is already selected returns Home to the main dashboard.

### V2-B1721 · v2 lines 9098–9098

Back/Forward arrows sit to the left of Home and navigate history inside the currently selected tab. Unavailable navigation buttons are disabled/greyed.

### V2-B1722 · v2 lines 9101–9102

Source lineage
main is a special top-level source root and is above all normal Branches.

### V2-B1723 · v2 lines 9105–9105

main may contain normal project source and Roots. main may NEVER contain Twigs, Leaves or Subroots.

### V2-B1724 · v2 lines 9108–9112

Normal development follows:
main
└─ Branch
   └─ Twig
      └─ Leaf

### V2-B1725 · v2 lines 9115–9115

A Branch is broadly analogous to a Git branch.

### V2-B1726 · v2 lines 9118–9118

A Twig is a nested development domain beneath a Branch.

### V2-B1727 · v2 lines 9121–9121

A Leaf is a nested development domain beneath a Twig.

### V2-B1728 · v2 lines 9124–9124

Stack does not use worktrees as its user-facing or internal parallel-development model.

### V2-B1729 · v2 lines 9127–9135

Example:
main
└─ 9to1 2027 Q1 Update
   ├─ Write
   │  ├─ New Font Switcher
   │  ├─ Editor 2.0
   │  └─ Persistent Word Counter
   ├─ Present
   └─ Data 2.0

### V2-B1730 · v2 lines 9138–9139

Cascade semantics
Inheritance cascades downward by default:

### V2-B1731 · v2 lines 9142–9142

main → Branch → Twig → Leaf.

### V2-B1732 · v2 lines 9145–9145

Changes committed to main automatically cascade into every Branch and then through descendants.

### V2-B1733 · v2 lines 9148–9148

Changes made at a Branch cascade into its Twigs and Leaves.

### V2-B1734 · v2 lines 9151–9151

This allows shared frameworks/donor changes to be implemented once at a higher domain rather than duplicated in every child.

### V2-B1735 · v2 lines 9154–9154

If a cascade conflicts with descendant work, propagation for the affected path/node pauses until the conflict is resolved.

### V2-B1736 · v2 lines 9157–9157

The effective source state of a Twig/Leaf is its own changes combined with inherited state from its ancestors.

### V2-B1737 · v2 lines 9160–9160

Deletion and rename semantics must be represented explicitly so inherited files do not incorrectly reappear after a child intentionally deletes or renames them.

### V2-B1738 · v2 lines 9163–9164

Active domain
Users do not switch branches in the traditional Git sense.

### V2-B1739 · v2 lines 9167–9167

Right-clicking any Branch/Twig/Leaf exposes Set Active.

### V2-B1740 · v2 lines 9170–9170

The active domain is only the default context used when opening an editor, running tests or building the project.

### V2-B1741 · v2 lines 9173–9173

Other Branches/Twigs/Leaves remain browseable and actionable at all times and can expose actions such as Open in Editor or Build without requiring the user to stash changes or change the active domain.

### V2-B1742 · v2 lines 9176–9176

If no Branch/Twig/Leaf is active, main becomes active automatically.

### V2-B1743 · v2 lines 9179–9187

Project sidebar
A project shows its whole lineage as one collapsible tree rather than hiding branches behind a branch selector:
Project
└─ Branches
   └─ Branch (+A -D)
      └─ Twigs
         └─ Twig (+A -D)
            └─ Leaves
               └─ Leaf (+A -D)

### V2-B1744 · v2 lines 9190–9190

Project, Branches, Twigs and Leaves are collapsible headers.

### V2-B1745 · v2 lines 9193–9195

Every Branch/Twig/Leaf label shows live diff totals, for example:
Notes Overhaul (+90 -20)
Displayed +A/-D totals MUST represent only the delta owned by that node relative to its effective parent. Inherited ancestor changes MUST NOT increase a child node’s own displayed totals.

### V2-B1746 · v2 lines 9200–9200

Selecting a node shows its effective source plus the node's diff against its parent. Changed filenames are visually status-coded, including green for additions/positive changes and red for removals/deletions, with clear states for renames and conflicts. Diff entries show the affected path and exact file/location.

### V2-B1747 · v2 lines 9203–9204

Local and remote state
Uncommitted/unpushed changes appear at the top of a node's change view in bold with a yellow up-arrow.

### V2-B1748 · v2 lines 9207–9207

Committed changes appear underneath.

### V2-B1749 · v2 lines 9210–9210

Any Branch/Twig/Leaf containing uncommitted/unpushed changes also shows the yellow up-arrow beside its tree label.

### V2-B1750 · v2 lines 9213–9213

A user setting can change this view to show only uncommitted/unpushed changes.

### V2-B1751 · v2 lines 9216–9216

If changes exist on the remote but not the current machine, show a yellow cloud icon beside the node.

### V2-B1752 · v2 lines 9219–9219

If remote and local changes conflict, the cloud turns red.

### V2-B1753 · v2 lines 9222–9223

Conflict handling
When a conflict is detected, Stack opens a popup listing the conflicting filenames.

### V2-B1754 · v2 lines 9226–9226

View details opens the exact conflicting hunks/locations.

### V2-B1755 · v2 lines 9229–9229

Summarise with AI sends the relevant conflict context to Dulche/Home and returns a plain-language summary of the conflict.

### V2-B1756 · v2 lines 9232–9232

Fix with AI asks Dulche to produce a resolution through typed source-control actions rather than opaque UI automation.

### V2-B1757 · v2 lines 9235–9239

After an AI fix, Stack presents:
* a summary of what Dulche changed;
* the resulting diff;
* Reject;
* Accept and Merge ▼.

### V2-B1758 · v2 lines 9242–9242

The dropdown under Accept and Merge contains Accept without Merging.

### V2-B1759 · v2 lines 9245–9245

Reject restores the pre-fix state.

### V2-B1760 · v2 lines 9248–9248

Accept without Merging keeps the resolved changes but does not complete the merge.

### V2-B1761 · v2 lines 9251–9251

Accept and Merge accepts the resolution and completes the merge.

### V2-B1762 · v2 lines 9254–9254

AI conflict resolutions must never silently merge without this explicit review step.

### V2-B1763 · v2 lines 9257–9258

Roots
A Root means: this file/folder/domain is structurally important and locked in Stack.

### V2-B1764 · v2 lines 9261–9261

Root state is stored in roots.json in the owning domain. Domain means main, Branch, Twig or Leaf.

### V2-B1765 · v2 lines 9264–9264

All files/paths listed in that domain's roots.json are locked by Stack according to Root rules.

### V2-B1766 · v2 lines 9267–9267

A Root is authoritative for descendants: child domains cannot edit, replace or override the protected content directly.

### V2-B1767 · v2 lines 9270–9270

Roots represent authoritative shared frameworks, donor source or other protected components. Unless changed through the Root/Subroot workflow, Root content MUST cascade unchanged into descendant domains and descendants MUST NOT override it directly.

### V2-B1768 · v2 lines 9275–9275

A Root may exist in main or another domain.

### V2-B1769 · v2 lines 9278–9278

Root enforcement must derive from stored metadata, not only from transient UI state.

### V2-B1770 · v2 lines 9281–9282

Freeze
Freeze is different from Root.

### V2-B1771 · v2 lines 9285–9285

Root means “important/authoritative; direct editing below this domain is not allowed.”

### V2-B1772 · v2 lines 9288–9288

Freeze means “out of scope for now.”

### V2-B1773 · v2 lines 9291–9291

When creating a Freeze, the user chooses exactly which descendant subdomains inherit the Freeze. It does not automatically apply to every child unless selected.

### V2-B1774 · v2 lines 9294–9294

A Freeze may have an expiry point/time/condition after which editing is restored automatically.

### V2-B1775 · v2 lines 9297–9300

Stack must clearly show:
* that a file/domain is frozen;
* which descendants the Freeze applies to;
* when it expires, if applicable.

### V2-B1776 · v2 lines 9303–9303

Freeze lifecycle is explicit. A Freeze records stable FreezeID, target Root/resource/domain, selected descendant scope, creator, reason, creation revision, optional expiry/condition and state. New descendants inherit an active Freeze only when they fall inside its configured scope policy. Early thaw requires the configured Freeze-management permission and is audited. Automatic expiry transitions the Freeze to Expired and restores editability only after Stack checks for in-progress operations and reconciles queued upstream changes. A user without Freeze-management permission cannot override or bypass an active Freeze.

### V2-B1777 · v2 lines 9308–9309

Subroots
A Subroot is the controlled mechanism for testing changes to Root files.

### V2-B1778 · v2 lines 9312–9314

Subroots sit in the same domain that owns the Root, for example:
Twig
└─ Twig Subroots

### V2-B1779 · v2 lines 9317–9317

A Subroot explicitly selects which Roots are in scope.

### V2-B1780 · v2 lines 9320–9320

Each Root file/folder can be included in only one active Subroot at a time within that domain. Stack must prevent overlapping Subroots from claiming the same Root.

### V2-B1781 · v2 lines 9323–9323

Root changes are developed/tested through the Subroot rather than by directly editing protected descendant copies.

### V2-B1782 · v2 lines 9326–9326

main can contain Roots but can NEVER contain Subroots.

### V2-B1783 · v2 lines 9329–9329

Subroot completion is a first-class reviewed operation. Completing a Subroot requires a diff against the owning Root, configured validation/check results, conflict resolution and the required approvals. On completion the accepted change is applied to the owning Root as one auditable Root update, descendants receive it through normal cascade semantics, and the Subroot releases its Root claims. The user may instead abandon the Subroot, which discards its proposed Root changes while preserving audit/history. A Subroot cannot be silently deleted while it owns unresolved changes or Root claims.

### V2-B1784 · v2 lines 9334–9344

Files-backed storage
For Files-backed projects, Stack uses explicit Stack-managed dot folders:
Project Name/
├─ .git/
├─ .branches/
│  └─ Branch/
│     └─ nested Stack structure
├─ .source/
│  └─ project source tree
└─ .roots/
   └─ roots.json

### V2-B1785 · v2 lines 9347–9347

Leading-dot paths are Stack-managed infrastructure. The UI MUST identify them as managed/internal state and MUST prevent ordinary project-editing flows from casually deleting or editing them. Explicit diagnostic/recovery tooling MAY expose controlled access.

### V2-B1786 · v2 lines 9352–9352

The exact nested on-disk schema for Branch/Twig/Leaf/Subroot domains and their per-domain metadata must be versioned and documented.

### V2-B1787 · v2 lines 9355–9355

Stack MUST validate its managed folders/manifests when opening/synchronising a project. If required infrastructure is missing, moved, altered or partially deleted, Stack MUST enter a recoverable/diagnostic state, identify the damaged objects and attempt deterministic reconstruction from retained metadata/object IDs where reconstruction is possible; it MUST NOT silently treat damaged infrastructure as ordinary project source.

### V2-B1788 · v2 lines 9360–9361

GitHub-backed storage
Each Stack project using GitHub maps to its own dedicated GitHub repository.

### V2-B1789 · v2 lines 9364–9364

Normal Stack Branches remain normal native Git branches in that repository.

### V2-B1790 · v2 lines 9367–9367

Twigs, Leaves and Subroot state are not represented as a proliferation of independent Git branches.

### V2-B1791 · v2 lines 9370–9370

Stack stores Twig/Leaf/Subroot nested state in protected Stack-managed companion storage in the same repository. The canonical Git ref for that companion branch is `stack/twigs-and-leaves-dependency`; the UI presents it as managed infrastructure (for example “Twigs and Leaves (DEPENDENCY)”) and MUST NOT expose it as an ordinary user Branch.

### V2-B1792 · v2 lines 9375–9375

This dependency branch is never surfaced as a normal Branch inside Stack. Stack interprets it and renders the actual Branch → Twig → Leaf hierarchy.

### V2-B1793 · v2 lines 9378–9378

The dependency branch must contain a prominent README explaining that it is Stack-managed infrastructure and must not be deleted, merged into normal branches, rebased, renamed or manually edited.

### V2-B1794 · v2 lines 9381–9381

It must also contain a Stack manifest/schema sufficient to identify the project, version the storage format, map Branch/Twig/Leaf/Subroot IDs and recover safely.

### V2-B1795 · v2 lines 9384–9384

If the dependency tree/manifest appears in a normal branch because somebody merged or moved it, Stack must recognise the signature, warn the user and recover/relink the dependency branch where possible rather than treating those files as normal project source.

### V2-B1796 · v2 lines 9387–9387

If the dependency branch is deleted, Stack MUST detect the missing managed ref and MUST attempt reconstruction from retained commit/object IDs, manifests and configured Files-backed recovery metadata. If complete reconstruction is impossible, Stack MUST preserve recoverable evidence, identify unrecoverable objects explicitly and block unsafe writes that could overwrite remaining recovery state.

### V2-B1797 · v2 lines 9392–9392

The GitHub adapter MUST preserve Stack semantics rather than mirror the Files backing layout literally. Branches map to native Git branches; Twig/Leaf/Subroot state maps to the protected Stack companion branch/manifest model defined above. Other entities MUST use a native GitHub/Git construct only when that construct preserves the canonical Stack identity, permissions and lifecycle semantics; otherwise Stack-managed metadata remains authoritative.

### V2-B1798 · v2 lines 9397–9398

Sweeping and pruning
Sweep collects the changes from all descendant Twigs/Leaves into the owning Branch root, then removes those descendant development domains.

### V2-B1799 · v2 lines 9401–9401

Prune removes/discards a Stack Branch and its descendants from Stack.

### V2-B1800 · v2 lines 9404–9404

Prune requires 2FA.

### V2-B1801 · v2 lines 9407–9407

main can never be pruned.

### V2-B1802 · v2 lines 9410–9410

Pruning must not immediately destroy the backing source. The underlying Files/GitHub data must remain recoverable/auditable according to the project's retention policy.

### V2-B1803 · v2 lines 9413–9413

Individual Twigs and Leaves can also be removed independently.

### V2-B1804 · v2 lines 9416–9416

Pruned domains enter a recoverable Deleted state for the project's configured retention period. Restore reactivates the same stable DomainID and backing references when their retained source objects remain valid. Permanent purge is a separate privileged operation, requires explicit confirmation and 2FA for protected project policies, and is blocked while retained objects are referenced by active descendants, releases, change requests, audit requirements or recovery points. Purge records what was removed and what immutable audit evidence remains.

### V2-B1805 · v2 lines 9421–9422

Public collaboration
Public Stack projects expose a dedicated Stack interface for source browsing, forks and contributions rather than forcing users into internal Files storage.

### V2-B1806 · v2 lines 9425–9425

Public projects provide project profiles, source browsing, history/releases, contributor identity, forks, Change Requests, notifications and Discover integration. Moderation supports project-level roles, report/block actions and removal of abusive public content without rewriting unrelated source history. Forks receive independent ProjectID/repository identity and record their source-project provenance. Contributions return through Change Requests against an explicitly selected destination domain; contributors never receive implicit access to private project components or Stack-managed dependency storage.

### V2-B1807 · v2 lines 9430–9430

Where project components have different public/private visibility, Stack must preserve hard security boundaries and never rely on UI hiding.

### V2-B1808 · v2 lines 9433–9434

Sites integration
Stack is closely integrated with Sites.

### V2-B1809 · v2 lines 9437–9437

At minimum, Sites must be able to use Stack projects as source projects, open the relevant source domain, and maintain source ownership/history rather than publishing opaque generated output.

### V2-B1810 · v2 lines 9440–9440

Sites binds deployments/previews to exact Stack ProjectID, DomainID and source revision. Branch/Twig/Leaf domains may create isolated preview deployments where policy permits; production deployments default to an approved Main revision unless the project explicitly authorises another release source. Build/deployment status is attached to the exact revision. Sites edits to source-backed projects are committed back into the currently bound Stack domain as ordinary reviewable source changes; Sites MUST NOT maintain an anonymous divergent copy of the project.

### V2-B1811 · v2 lines 9445–9446

Gitea/Gitnuro donor parity
Gitea is the primary donor and must be forked from the latest appropriate source at implementation time.
