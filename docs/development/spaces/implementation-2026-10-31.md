# Spaces implementation, 31 October 2026

Current authority: canonical v2, SP-003 (manage and recover Spaces), UX27-SPACES-001
(retain contextual conversation environment), and the owner's latest dedicated Spaces implementation request.
The actual Browse Spaces session composes `SpacesHomePage` from `SpacesHome.cui` and uses
`NewChatPage` through its existing factory. Legacy `NativeSpacesPage` is not the implementation target.

## Pinned archive and recovery flow

1. In Manage Spaces, the user archives a selected custom Space. Its existing stable ID,
   conversations, source references, configuration and nested hierarchy are preserved.
2. The manager exposes a separate Archived Spaces list. Archived entries never appear in the
   normal navigation rail, current conversation selection or candidate parent list.
3. Each archived row identifies its Space and offers Restore. The user restores that exact row
   at its displayed revision; a stale row refuses instead of silently changing newer state.
4. The existing registry enforces parent-before-child recovery. A blocked restore keeps the
   item archived and explains the required parent recovery. No parent is silently restored.
5. Success reintroduces that same Space in navigation, removes the archived row and reports the
   result. The currently open Space/conversation remains selected; restoration does not create
   or open a conversation or grant access to its sources.
6. Restart/fresh controller reads the same archive state from the canonical settings store.

Evidence: source inspection only at flow pinning. Focused compilation/tests are owned by Root's
serial SDK queue. Native mounted screenshots and independent acceptance remain NOT RUN.

## Pinned source management flow

Source inspection found that Add source writes the canonical `SpaceDefinition.Files` collection,
while the mounted manager renders only `ContextReferences`. The added file is therefore invisible.

1. In the selected custom Space's manager, Add source opens the existing local file picker.
2. Cancel preserves the selected Space and its source list. Selection uses the existing registry
   file reference operation with the displayed Space revision checked at the write boundary.
3. The manager displays each actual local reference's filename, path and requested access mode.
   It does not claim the file is indexed, accessible or ingested merely because a reference is saved.
4. Remove source targets the exact displayed reference of the current Space. It removes only that
   inclusion reference using the current Space revision. It does not delete the file or other sources.
5. A changed Space/reference refuses stale mutation and retains the displayed data for refresh.
   Fresh registry/controller reads restore the persisted source list.

This next unit remains planned until the serial compiler releases the live files.

## Pinned sibling ordering flow

1. In Manage Spaces, the user selects a custom Space and sees its current parent plus Move up
   and Move down. These actions reorder only active custom siblings under that same parent.
2. The first sibling cannot move up; the last cannot move down. Built-in order remains protected.
3. Each action captures the selected Space revision and the displayed ordered sibling IDs.
   The existing registry Move operation checks both inside its atomic mutation before writing.
4. The same Space remains selected and keeps its parent, children, sources and conversations.
   The normal rail reflects the saved order; fresh registry reads preserve it after restart.
5. If the order or selected revision changed elsewhere, the action refuses and asks for refresh
   rather than moving an unexpected sibling. Source code for this next unit is prepared outside
   the live checkout while Root's serial compiler validates the archive/source unit.

## Pinned navigation preferences flow

1. The existing icon rail remains the default, with the current canonical Space order. In
   Manage Spaces the user chooses Icon rail or Dropdown, pins/unpins actual Space IDs and
   changes the order of pinned entries. All active Spaces remain in the manager and switcher.
2. Dropdown mode replaces the rail with a labelled Space selector in the existing persistent
   app header plus Manage Spaces. It remains available when the contextual sidebar is collapsed.
   Choosing a different Space uses the same direct-open command as the rail; merely switching
   layouts/pins does not navigate, create a chat or change the selected Space.
3. Preferences persist under the same SpaceRegistry profile store and original actor/CAS
   admission. They contain presentation mode and ordered IDs, not duplicate Space records or grants.
4. Archived or missing IDs may remain as inert saved pins but never produce active navigation
   rows. Recovery reuses the same ID and position; no missing Space is recreated from preferences.
5. A stale preference revision refuses without overwriting newer choices. Unpinning the current
   Space keeps it open; dropdown/manager still provide a route to it. Fresh controller/registry
   reads restore the saved layout and order. Draft and workspace ownership remain unchanged.

Navigation preference and sibling-order source are prepared in scratch only until the current
archive/source test unit compiles and passes its focused checks.

## Built-in source access correction

Canonical Sources and context rules apply to normal Chat, Research, Translate and other built-in
Spaces as well as custom Spaces. Built-in protection covers structure/instructions/graphs, not the
ordinary act of attaching or removing a source. The existing registry already distinguishes these
operations. The initial manager incorrectly reused structural `CanEdit` for source controls.
A separate source capability will enable source inspection/add/remove for an active selected Space
while retaining protected built-in rename, archive, hierarchy and graph behavior. This uses the same
host admission and registry mutation checks; a source reference never grants file access.

## Narrow-window correction pinned from actual 390px capture

The full initial headless suite produced a real 390px screenshot with only 40px left for the manager because the 70px rail and 280px contextual sidebar stayed simultaneously expanded. This is an observed presentation defect, not an acceptance pass.

1. Below the existing chat compact breakpoint (720px), keep the user's persisted rail/dropdown choice and current Space unchanged, and initially show the main content with the contextual sidebar closed.
2. The existing Expand sidebar action opens that same contextual sidebar in the available content area. Its Collapse action returns to the same mounted content; the chat, draft, source selection and task remain mounted and unchanged.
3. Selecting a Space/conversation or opening the manager closes only the temporary compact sidebar presentation. Required destinations remain available via rail or dropdown.
4. Resizing back to desktop restores the user's ordinary sidebar collapse state. Window size and temporary compact visibility are not profile preferences and never write to the registry.
5. Header controls and manager actions must fit the actual viewport; tests measure main-content width and execute real pointer input, not merely assert a compact flag. The native desktop page must pass its actual content Bounds width to the shared presentation bindings.

## Effective policy and inherited source projection

The manager currently calls local fields “Effective model”, “Web access policy” and “Memory scope”. Those labels are wrong when an ancestor supplies the model or restricts web/memory. The correction must use the same `SpacePolicyResolver` as execution against the exact registry snapshot already displayed.

1. Opening/selecting a Space computes its effective policy without changing saved configuration, enabling inheritance, or reading any source body.
2. Display the effective model/web/memory outcomes, including disabled categories. Local source rows retain their existing remove action.
3. Show effective inherited references separately with their ancestor Space and canonical owning app/revision. No local Remove button is offered for a source owned by an ancestor; the user edits that owning Space or the existing explicit inheritance configuration.
4. A nearest local denied reference suppresses the inherited reference exactly as the canonical resolver already requires. Persisted permission/index observations are labelled as recorded; actual source use still performs owner authorization.
