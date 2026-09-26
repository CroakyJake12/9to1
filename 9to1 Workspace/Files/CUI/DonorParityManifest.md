# Files donor parity manifest

## Source and scope

- Donor: Files by Files Community, included under `Source/Files/`.
- Selected upstream revision: `21d407b51dbc2b1bd2733ef615d727345d5dd16f`.
- Source integrity: the imported `src` and `tests` trees match the source provenance recorded in `Source/DONOR-PROVENANCE.md`.
- Product scope: applicable Files/WinUI user workflows are preserved when ported to the CUI shell. CUI replaces rendering and framework integration; it does not narrow the donor feature baseline.
- Proprietary donor AI: **Replace** with Dulche and supported 9to1 Files actions.
- Donor subscription/account infrastructure: **Not applicable** unless an independent 9to1 requirement applies.

## Feature classifications

“Classification” records the required parity treatment. “CUI status” records the current evidence and is not a claim of parity.

| Donor feature area | Classification | CUI status |
|---|---|---|
| Sidebar, locations, navigation history, tabs, breadcrumbs and address bar | Preserve | Not implemented or parity-verified in the current CUI surface |
| Toolbar, command menu, New menu, context menus and keyboard shortcuts | Preserve | Not implemented or parity-verified in the current CUI surface |
| File and folder listing, selection, grid/list/details layouts, sorting, grouping and columns | Preserve | Not implemented or parity-verified in the current CUI surface |
| Search, tags, favourites, properties, previews and thumbnails | Preserve | Not implemented or parity-verified in the current CUI surface |
| Rename, create, copy, move, duplicate, delete/trash and file-operation progress | Preserve | Not implemented or parity-verified in the current CUI surface |
| Conflict prompts, clipboard, drag/drop, archives and destination handling | Preserve | Not implemented or parity-verified in the current CUI surface |
| Local disks, removable media, network locations and provider-specific constraints | Preserve | Not implemented or parity-verified in the current CUI surface |
| Settings, folder/view preferences, accessibility, localization and session restoration | Preserve | Not implemented or parity-verified in the current CUI surface |
| Proprietary donor AI or donor-only cloud AI service | Replace | Route through Dulche with Files action APIs and privacy/permission checks |
| Donor-specific account and subscription system | Not applicable | 9to1 Home/account services own identity and access |

## 9to1 extensions

9to1 Drive, stable hosted FileID/FolderID identity, provider capability declarations, durable push sync, offline operation journals, hydration states, revision history, Files-owned ACLs, recoverable Trash, cross-app artifact references, typed automation actions and folder colours are 9to1 additions. They extend the donor information architecture and do not count as permission to remove donor behaviours.

## Validation required before claiming parity

The CUI port must be exercised against the listed donor journeys using the donor source as the behavioural reference. This manifest records the required classification and current gap; it does not substitute for implementation, UI testing, or visual comparison.
