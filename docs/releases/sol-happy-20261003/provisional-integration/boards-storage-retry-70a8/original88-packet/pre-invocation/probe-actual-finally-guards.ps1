$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($args[0], [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw 'Actual source has parse errors.' }
$try = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.TryStatementAst] -and $null -ne $node.Finally -and $node.Finally.Extent.Text.Contains('if ($restoreOwnedPrimaryAttributes)') }, $true)
if ($null -eq $try) { throw 'Missing actual outer finally guard.' }
$body = $try.Finally.Extent.Text.Substring(1, $try.Finally.Extent.Text.Length - 2)
$observations = @()
foreach ($case in @('attribute-restoration-throws', 'stream-drain-failed', 'forced-cleanup', 'cleanup-failed', 'clean')) {
    $result = [ordered]@{ status = 'CONTROLLED_PASS_BEFORE_FINALLY'; forcedCleanup = $false }
    $Scenario = 'storage-retry'; $process = $null; $exitCode = 0
    $restoreOwnedPrimaryAttributes = $case -ceq 'attribute-restoration-throws'
    function Restore-OwnedPrimaryAttributes { throw 'ControlledOwnedAttributeRestorationFailure' }
    function Write-Result { }
    if ($case -ceq 'stream-drain-failed') { $result.cleanupDrainFailure = 'ControlledIncompleteDrain' }
    if ($case -ceq 'forced-cleanup') { $result.forcedCleanup = $true }
    if ($case -ceq 'cleanup-failed') { $result.cleanupFailure = 'ControlledOwnProcessCleanupFailure' }
    # Exact actual source finally body; controlled external outcome only.
    Invoke-Expression $body
    $expectedBlocked = $case -cne 'clean'
    $blocked = $exitCode -eq 1 -and $result.status -ceq 'FAILED_OR_BLOCKED_UNACCEPTED'
    if ($blocked -ne $expectedBlocked) { throw "Actual finally policy mismatch: $case" }
    $observations += [ordered]@{ case = $case; expectedBlocked = $expectedBlocked; observedBlocked = $blocked; exitCode = $exitCode; status = $result.status }
}
[ordered]@{ status = 'ACTUAL_OUTER_FINALLY_5_CONTROLS_PASS'; controls = $observations; qualification = 'Exact actual finally body invoked against controlled restoration/cleanup outcomes, no process/file/UIA/provider operation. Policy refusal controls only; actual NTFS/read-only native UI remains NOT_RUN.' } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $args[1] -Encoding UTF8
