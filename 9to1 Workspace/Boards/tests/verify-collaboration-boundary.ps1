$ErrorActionPreference = 'Stop'

$boards = Split-Path -Parent $PSScriptRoot
$contract = Join-Path $boards 'contract/HavenBoardCollaboration.cs'
$coordinator = Join-Path $boards 'hui/HavenBoardCollaborationCoordinator.cs'
$contractTests = Join-Path $boards 'tests/HavenBoardCollaborationContractTests.cs'
$coordinatorTests = Join-Path $boards 'hui-tests/HavenBoardCollaborationCoordinatorTests.cs'

$required = @($contract, $coordinator, $contractTests, $coordinatorTests)
$missing = $required | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
if ($missing) {
    throw "Missing Boards collaboration boundary files: $($missing -join ', ')"
}

$contractText = Get-Content -LiteralPath $contract -Raw
if ($contractText -match 'HttpClient|WebSocket|System\.Net\.Sockets|TcpClient|UdpClient|https?://') {
    throw 'Neutral collaboration contract/default adapter must not contain a network implementation.'
}
if ($contractText -notmatch 'MaxCommandsPerBatch\s*=\s*256' -or
    $contractText -notmatch 'Array\.AsReadOnly\(materialized\)' -or
    $contractText -notmatch '_\s*=>\s*false') {
    throw 'Collaboration batches must remain bounded, immutable after validation, and fail closed on unknown commands.'
}
if ($contractText -match '(?s)IsStructuralSyncCommand\(HavenBoardCommand command\).*?AddAttachmentCommand\s*=>\s*true') {
    throw 'Attachment mutation must not cross the structural collaboration boundary.'
}
if ($contractText -notmatch 'DenyAllHavenBoardPermissionProvider' -or
    $contractText -notmatch 'OwnerOnlyHavenBoardPermissionProvider' -or
    $contractText -notmatch 'IHavenBoardPermissionProvider') {
    throw 'Collaboration boundary must retain explicit fail-closed permission providers.'
}
if ($contractText -notmatch 'DisabledHavenBoardSyncAdapter' -or
    $contractText -notmatch 'public bool IsEnabled => false' -or
    $contractText -notmatch 'HavenBoardSyncPublishStatus\.Disabled') {
    throw 'Default sync adapter must remain explicitly disabled and must not claim delivery.'
}

$coordinatorText = Get-Content -LiteralPath $coordinator -Raw
if ($coordinatorText -match 'HttpClient|WebSocket|System\.Net\.Sockets|TcpClient|UdpClient|https?://') {
    throw 'Collaboration coordinator must not contain transport/network behavior.'
}
if ($coordinatorText -notmatch 'HavenBoardCollaborationBatch\.Create\(' -or
    $coordinatorText -notmatch '_permissions\.AuthorizeAsync\(' -or
    $coordinatorText -notmatch 'session\.ExecuteBatchAsync\(') {
    throw 'Inbound collaboration must revalidate public DTOs, authorize commands, and use the atomic session batch boundary.'
}
if ($coordinatorText -notmatch 'batch\.BaseVersion != session\.Snapshot\.Version' -or
    $coordinatorText -notmatch '_appliedMutationIds\.Contains\(batch\.MutationId\)' -or
    $coordinatorText -notmatch 'MaxRememberedMutationIds\s*=\s*4096') {
    throw 'Inbound collaboration must retain strict conflict and bounded replay protection.'
}

$contractTestsText = Get-Content -LiteralPath $contractTests -Raw
if ($contractTestsText -notmatch 'Batch_factory_freezes_structural_commands_and_retains_identity' -or
    $contractTestsText -notmatch 'Batch_factory_rejects_attachment_smuggling_unsafe_ids_and_oversized_batches' -or
    $contractTestsText -notmatch 'Disabled_sync_adapter_never_claims_remote_delivery_or_returns_remote_data' -or
    $contractTestsText -notmatch 'Owner_only_permission_provider_allows_only_exact_configured_actor') {
    throw 'Neutral collaboration tests must cover immutability, attachment exclusion, disabled transport, and permissions.'
}

$coordinatorTestsText = Get-Content -LiteralPath $coordinatorTests -Raw
if ($coordinatorTestsText -notmatch 'Default_deny_policy_rejects_remote_mutation_without_save' -or
    $coordinatorTestsText -notmatch 'Authorised_structural_batch_commits_once_and_survives_reopen' -or
    $coordinatorTestsText -notmatch 'Stale_batch_is_conflict_and_never_persists' -or
    $coordinatorTestsText -notmatch 'Caller_constructed_attachment_batch_is_revalidated_and_rejected_before_save' -or
    $coordinatorTestsText -notmatch 'Applied_mutation_id_is_replay_protected') {
    throw 'Collaboration coordinator tests must cover deny, authorised atomic apply, conflict, DTO smuggling, and replay protection.'
}

Write-Host 'Haven Boards collaboration boundary safety checks passed.'
