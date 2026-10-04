$ErrorActionPreference = 'Stop'

$boards = Split-Path -Parent $PSScriptRoot

$pubspec = Join-Path $boards 'appflowy_poc/pubspec.yaml'
$thirdParty = Join-Path $boards 'THIRD_PARTY.md'
$contract = Join-Path $boards 'contract/HavenBoardContract.cs'
$contractProject = Join-Path $boards 'contract/CakeOS.Apps.Boards.Contract.csproj'
$store = Join-Path $boards 'contract/JsonFileHavenBoardStore.cs'
$attachmentStore = Join-Path $boards 'contract/ContentAddressedHavenBoardAttachmentStore.cs'
$hui = Join-Path $boards 'hui/HavenBoardsHuiScene.cs'
$freeformHui = Join-Path $boards 'hui/HavenBoardsFreeformHuiScene.cs'
$huiSession = Join-Path $boards 'hui/HavenBoardsHuiSession.cs'
$huiProject = Join-Path $boards 'hui/CakeOS.Apps.Boards.Hui.csproj'
$huiTestProject = Join-Path $boards 'hui-tests/CakeOS.Apps.Boards.Hui.Tests.csproj'
$huiTests = Join-Path $boards 'hui-tests/HavenBoardsHuiSceneTests.cs'
$hierarchyHuiTests = Join-Path $boards 'hui-tests/HavenBoardsHierarchyHuiTests.cs'
$freeformHuiTests = Join-Path $boards 'hui-tests/HavenBoardsFreeformHuiSceneTests.cs'
$harness = Join-Path $boards 'appflowy_poc/lib/main.dart'
$flutterTests = Join-Path $boards 'appflowy_poc/test/appflowy_board_poc_test.dart'
$testProject = Join-Path $boards 'tests/CakeOS.Apps.Boards.Tests.csproj'
$contractTests = Join-Path $boards 'tests/HavenBoardContractTests.cs'
$crudContractTests = Join-Path $boards 'tests/HavenBoardGroupCardCrudTests.cs'
$crudHuiTests = Join-Path $boards 'hui-tests/HavenBoardsCrudHuiTests.cs'
$freeformContractTests = Join-Path $boards 'tests/HavenBoardFreeformContractTests.cs'

$required = @(
    $pubspec,
    $thirdParty,
    $contract,
    $contractProject,
    $store,
    $attachmentStore,
    $hui,
    $freeformHui,
    $huiSession,
    $huiProject,
    $huiTestProject,
    $huiTests,
    $hierarchyHuiTests,
    $freeformHuiTests,
    $harness,
    $flutterTests,
    $testProject,
    $contractTests,
    $crudContractTests,
    $crudHuiTests,
    $freeformContractTests
)
$missing = $required | Where-Object { -not (Test-Path -LiteralPath $_) }
if ($missing) { throw "Missing Haven Boards foundation files: $($missing -join ', ')" }

$expectedCommit = '804d7898ac0becabf73e45527baf5d5c573cd6bb'
$pubspecText = Get-Content -LiteralPath $pubspec -Raw
if ($pubspecText -notmatch [regex]::Escape($expectedCommit)) {
    throw "appflowy-board is not pinned to expected commit $expectedCommit"
}
if ($pubspecText -match '(?m)^\s*ref:\s*(main|master)\s*$') {
    throw 'appflowy-board must not track a moving branch.'
}

$thirdPartyText = Get-Content -LiteralPath $thirdParty -Raw
if ($thirdPartyText -notmatch 'MPL-?2\.0|Mozilla Public License 2\.0') {
    throw 'MPL 2.0 selection/provenance is missing from THIRD_PARTY.md.'
}

$contractText = Get-Content -LiteralPath $contract -Raw
if ($contractText -match '(?mi)^\s*using\s+(AppFlowy|Avalonia)\b|package:(appflowy_board|flutter)') {
    throw 'Neutral Haven board contract contains a renderer/vendor import.'
}
if ($contractText -notmatch 'AddAttachmentCommand' -or $contractText -notmatch 'RemoveAttachmentCommand') {
    throw 'Neutral Haven board contract must retain typed attachment metadata commands.'
}
if ($contractText -notmatch 'SetCardParentCommand' -or
    $contractText -notmatch 'ValidateParentChain' -or
    $contractText -notmatch 'public static void Validate\(HavenBoardSnapshot snapshot\)') {
    throw 'Neutral Haven board contract must retain typed hierarchy mutation plus snapshot/cycle validation.'
}
if ($contractText -notmatch 'cardsById\.TryAdd' -or
    $contractText -notmatch 'references missing parent' -or
    $contractText -notmatch 'hierarchy contains a cycle') {
    throw 'Hierarchy validation must reject duplicate IDs, missing parents, and cycles.'
}
if ($contractText -notmatch 'CreateGroupCommand' -or
    $contractText -notmatch 'RemoveGroupCommand' -or
    $contractText -notmatch 'RenameCardCommand' -or
    $contractText -notmatch 'RemoveCardCommand' -or
    $contractText -notmatch 'OrphanChildren' -or
    $contractText -notmatch 'ValidateNewGroupId') {
    throw 'Neutral Haven board contract must retain donor-parity group/card CRUD with orphan-safe hierarchy.'
}
if ($contractText -notmatch 'HavenBoardFreeformLayout' -or
    $contractText -notmatch 'SetFreeformCardFrameCommand' -or
    $contractText -notmatch 'RemoveFreeformCardFrameCommand' -or
    $contractText -notmatch 'ValidateFreeformItem') {
    throw 'Neutral Haven board contract must retain renderer-independent freeform layout and typed frame commands.'
}
if ($contractText -notmatch 'FreeformCoordinateLimit' -or
    $contractText -notmatch 'FreeformMaxDimension' -or
    $contractText -notmatch 'Freeform layout references missing card' -or
    $contractText -notmatch 'duplicate frame') {
    throw 'Freeform validation must retain bounded geometry and card/frame identity checks.'
}

$huiText = Get-Content -LiteralPath $hui -Raw
if ($huiText -match '(?mi)^\s*using\s+(AppFlowy|Avalonia)\b|package:(appflowy_board|flutter)') {
    throw 'HUI board scene crossed the renderer/vendor import boundary.'
}
if ($huiText -notmatch '(?m)^using Haven\.UI;') {
    throw 'HUI board scene is not targeting the Haven UI runtime.'
}
if ($huiText -notmatch 'HavenBoardCommand') {
    throw 'HUI board scene must emit typed Haven board commands.'
}
if ($huiText -notmatch 'SetState\(HavenElementState\.Disabled,\s*!enabled\)' -or
    $huiText -notmatch 'Accessibility\.Enabled\s*=\s*enabled') {
    throw 'HUI board controls must synchronize Enabled, accessibility, and Disabled state.'
}
if ($huiText -notmatch 'Nested card') {
    throw 'HUI board scene must preserve hierarchy presentation for nested cards.'
}
if ($huiText -notmatch 'RenameGroupCommand' -or
    $huiText -notmatch 'RenameCardCommand' -or
    $huiText -notmatch 'RemoveCardCommand' -or
    $huiText -notmatch 'RemoveGroupCommand' -or
    $huiText -notmatch 'CreateGroupCommand') {
    throw 'HUI board scene must emit donor-parity group/card CRUD commands, not movement alone.'
}
if ($huiText -notmatch 'HavenInput = Haven\.UI\.Components\.Input' -or
    $huiText -notmatch 'Save group name' -or
    $huiText -notmatch 'Save card name' -or
    $huiText -notmatch 'Add group to board') {
    throw 'HUI board scene must offer working rename/add/delete controls through shared HUI primitives.'
}

$freeformHuiText = Get-Content -LiteralPath $freeformHui -Raw
if ($freeformHuiText -match '(?mi)^\s*using\s+(AppFlowy|Avalonia)\b|package:(appflowy_board|flutter)') {
    throw 'Freeform HUI scene crossed the renderer/vendor import boundary.'
}
if ($freeformHuiText -notmatch '(?m)^using Haven\.UI;' -or
    $freeformHuiText -notmatch 'HavenCanvas = Haven\.UI\.Components\.Canvas') {
    throw 'Freeform board must render through the real HUI Canvas primitive.'
}
if ($freeformHuiText -notmatch 'SetFreeformCardFrameCommand' -or
    $freeformHuiText -notmatch 'NudgeDistance' -or
    $freeformHuiText -notmatch 'HavenProperties\.Left' -or
    $freeformHuiText -notmatch 'HavenProperties\.Top') {
    throw 'Freeform HUI must project spatial geometry and emit typed nudge commands.'
}
if ($freeformHuiText -notmatch 'SetState\(HavenElementState\.Disabled,\s*!enabled\)' -or
    $freeformHuiText -notmatch 'Accessibility\.Enabled\s*=\s*enabled') {
    throw 'Freeform HUI nudge controls must synchronize Enabled, accessibility, and Disabled state.'
}

$huiSessionText = Get-Content -LiteralPath $huiSession -Raw
if ($huiSessionText -match 'HttpClient|WebSocket|https?://') {
    throw 'Composed HUI session must remain local-only and contain no network dependency.'
}
if ($huiSessionText -notmatch 'Scene\.CommandRequested \+= OnSceneCommandRequested' -or
    $huiSessionText -notmatch 'await _store\.SaveAsync\(updated' -or
    $huiSessionText -notmatch 'PublishDurableSnapshot\(updated') {
    throw 'Composed HUI session must bind typed scene commands through durable storage.'
}
if ($huiSessionText -notmatch 'FreeformScene\.CommandRequested \+= OnSceneCommandRequested' -or
    $huiSessionText -notmatch 'FreeformScene\.SetSnapshot\(snapshot\)') {
    throw 'Composed HUI session must route freeform HUI commands through the same durable snapshot boundary.'
}
if ($huiSessionText -notmatch 'HavenBoardReducer\.Validate\(snapshot\)') {
    throw 'Composed HUI session must validate persisted board hierarchy/freeform state before rendering.'
}
$sessionSaveIndex = $huiSessionText.IndexOf('await _store.SaveAsync(updated', [System.StringComparison]::Ordinal)
$sessionPublishIndex = $huiSessionText.IndexOf('PublishDurableSnapshot(updated', [System.StringComparison]::Ordinal)
if ($sessionSaveIndex -lt 0 -or $sessionPublishIndex -lt 0 -or $sessionSaveIndex -gt $sessionPublishIndex) {
    throw 'Composed HUI session must persist a mutation before publishing it as the visible snapshot.'
}
if ($huiSessionText -notmatch '(?s)private void PublishDurableSnapshot\(HavenBoardSnapshot snapshot.*?FreeformScene\.SetSnapshot\(snapshot\)') {
    throw 'Composed HUI session must persist a mutation before publishing it to the freeform projection.'
}
$huiProjectText = Get-Content -LiteralPath $huiProject -Raw
if ($huiProjectText -notmatch 'HavenUiProjectPath' -or
    $huiProjectText -notmatch 'RequireRealHavenUi' -or
    $huiProjectText -notmatch 'Haven\.UI\.csproj') {
    throw 'HUI build project must fail closed unless a real Haven.UI project is supplied.'
}
if ($huiProjectText -match '<PackageReference[^>]+(?:Avalonia|Flutter|AppFlowy)') {
    throw 'HUI product project must not take a renderer/vendor package dependency.'
}

$huiTestProjectText = Get-Content -LiteralPath $huiTestProject -Raw
if ($huiTestProjectText -notmatch 'CakeOS\.Apps\.Boards\.Hui\.csproj' -or
    $huiTestProjectText -notmatch 'xunit') {
    throw 'Executable HUI scene test project is not wired to the real-HUI compile project.'
}

$huiTestsText = Get-Content -LiteralPath $huiTests -Raw
if ($huiTestsText -notmatch 'Disabled_keyboard_move_cannot_emit_command' -or
    $huiTestsText -notmatch 'Enabled_keyboard_move_emits_typed_neutral_command') {
    throw 'HUI tests must cover enabled and disabled keyboard command paths.'
}
if ($huiTestsText -notmatch 'Open_execute_dispose_reopen_preserves_durable_snapshot_and_scene' -or
    $huiTestsText -notmatch 'Keyboard_scene_command_flushes_to_disk_and_survives_reopen') {
    throw 'HUI tests must cover composed durable reopen and keyboard-originated persistence.'
}
if ($huiTestsText -notmatch 'Attachment_blob_metadata_and_hui_count_survive_reopen') {
    throw 'HUI tests must cover attachment blob/metadata survival across reopen.'
}

$hierarchyHuiTestsText = Get-Content -LiteralPath $hierarchyHuiTests -Raw
if ($hierarchyHuiTestsText -notmatch 'Hierarchy_parent_and_nested_marker_survive_move_and_reopen' -or
    $hierarchyHuiTestsText -notmatch 'Session_open_rejects_persisted_missing_parent_before_render') {
    throw 'HUI tests must cover durable hierarchy/move presentation and fail-closed malformed hierarchy open.'
}

$freeformHuiTestsText = Get-Content -LiteralPath $freeformHuiTests -Raw
if ($freeformHuiTestsText -notmatch 'Explicit_freeform_frame_is_projected_to_real_hui_canvas_geometry' -or
    $freeformHuiTestsText -notmatch 'Keyboard_nudge_emits_typed_neutral_frame_command' -or
    $freeformHuiTestsText -notmatch 'Nudge_beyond_coordinate_limit_is_disabled_for_keyboard_and_accessibility' -or
    $freeformHuiTestsText -notmatch 'Freeform_keyboard_nudge_flushes_to_disk_and_survives_reopen') {
    throw 'HUI tests must cover exact freeform geometry, keyboard commands, boundary disablement, and durable reopen.'
}

$storeText = Get-Content -LiteralPath $store -Raw
if ($storeText -notmatch '\.9to1board' -or $storeText -notmatch '\.bak' -or $storeText -notmatch 'flushToDisk:\s*true') {
    throw 'Local-first store must retain .9to1board backup recovery and durable flush semantics.'
}
if ($storeText -match 'HttpClient|WebSocket|https?://') {
    throw 'Local-first board store must not contain a network dependency.'
}

$attachmentStoreText = Get-Content -LiteralPath $attachmentStore -Raw
if ($attachmentStoreText -match 'HttpClient|WebSocket|https?://') {
    throw 'Attachment store must remain local-only and contain no network dependency.'
}
if ($attachmentStoreText -notmatch 'IncrementalHash\.CreateHash\(HashAlgorithmName\.SHA256\)' -or
    $attachmentStoreText -notmatch 'ReferencePrefix = "sha256:"' -or
    $attachmentStoreText -notmatch 'flushToDisk:\s*true') {
    throw 'Attachment store must remain content-addressed and durably flushed.'
}
if ($attachmentStoreText -notmatch 'Path\.GetFileName' -or
    $attachmentStoreText -notmatch 'DefaultMaxAttachmentBytes' -or
    $attachmentStoreText -notmatch 'RejectLinkOrReparsePoint') {
    throw 'Attachment store must retain display-name isolation, bounded imports, and link/reparse protection.'
}

$harnessText = Get-Content -LiteralPath $harness -Raw
if ($harnessText -notmatch 'package:appflowy_board/appflowy_board.dart') {
    throw 'Flutter proof harness is not using appflowy-board.'
}
if ($harnessText -notmatch 'this\.enablePersistence\s*=\s*true') {
    throw 'Flutter proof harness must keep local persistence enabled by default.'
}

$flutterTestsText = Get-Content -LiteralPath $flutterTests -Raw
if ($flutterTestsText -notmatch 'HavenBoardsPocApp\(enablePersistence:\s*false\)') {
    throw 'Flutter widget tests must disable filesystem persistence and test UI behavior deterministically.'
}
if ($flutterTestsText -notmatch 'find\.byType\(AppFlowyBoard\)') {
    throw 'Flutter widget tests must prove the real AppFlowy Board is mounted.'
}

$testProjectText = Get-Content -LiteralPath $testProject -Raw
if ($testProjectText -notmatch 'CakeOS\.Apps\.Boards\.Contract\.csproj' -or $testProjectText -notmatch 'xunit') {
    throw 'Executable contract test project is not wired to the board contract.'
}

$contractTestsText = Get-Content -LiteralPath $contractTests -Raw
if ($contractTestsText -notmatch 'Import_is_content_addressed_deduplicated_and_display_name_cannot_escape_storage' -or
    $contractTestsText -notmatch 'Existing_deduplicated_blob_must_still_match_its_digest') {
    throw 'Contract tests must cover content-addressing/path isolation and tamper detection.'
}
if ($contractTestsText -notmatch 'Parent_cycle_is_rejected_without_publishing_mutated_snapshot' -or
    $contractTestsText -notmatch 'Moving_nested_card_between_groups_preserves_parent_identity' -or
    $contractTestsText -notmatch 'Snapshot_validator_rejects_missing_parent_existing_cycle_and_duplicate_ids') {
    throw 'Contract tests must cover hierarchy cycle rejection, move preservation, and malformed snapshot validation.'
}

$freeformContractTestsText = Get-Content -LiteralPath $freeformContractTests -Raw
if ($freeformContractTestsText -notmatch 'Freeform_frame_can_be_added_replaced_and_removed' -or
    $freeformContractTestsText -notmatch 'Freeform_layout_rejects_missing_cards_duplicate_frames_and_non_finite_or_unsafe_geometry' -or
    $freeformContractTestsText -notmatch 'Lane_moves_and_hierarchy_changes_preserve_freeform_geometry' -or
    $freeformContractTestsText -notmatch 'Freeform_layout_round_trips_through_local_store') {
    throw 'Contract tests must cover freeform lifecycle, validation, structured-move preservation, and local persistence.'
}

$crudContractTestsText = Get-Content -LiteralPath $crudContractTests -Raw
if ($crudContractTestsText -notmatch 'Create_group_appends_with_stable_id_and_bumps_version' -or
    $crudContractTestsText -notmatch 'Remove_group_drops_cards_frames_and_orphans_external_children' -or
    $crudContractTestsText -notmatch 'Remove_card_drops_frame_and_orphans_children_without_touching_siblings' -or
    $crudContractTestsText -notmatch 'Rename_card_updates_title_and_normalises_blank' -or
    $crudContractTestsText -notmatch 'Event_adapter_maps_donor_crud_shapes_to_typed_commands') {
    throw 'Contract tests must cover donor-parity group/card CRUD, orphan safety, and adapter mapping.'
}

$crudHuiTestsText = Get-Content -LiteralPath $crudHuiTests -Raw
if ($crudHuiTestsText -notmatch 'Group_rename_input_commits_typed_command_with_edited_text' -or
    $crudHuiTestsText -notmatch 'Card_rename_and_delete_emit_typed_commands_with_edited_text' -or
    $crudHuiTestsText -notmatch 'Crud_scene_commands_flush_to_disk_and_survive_reopen' -or
    $crudHuiTestsText -notmatch 'Remove_group_flushes_to_disk_and_survives_reopen') {
    throw 'HUI tests must cover working rename/delete/add controls and durable reopen.'
}

Write-Host 'Haven Boards AppFlowy foundation static checks passed.'
