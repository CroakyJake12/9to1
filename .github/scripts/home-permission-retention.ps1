[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [Parameter(Mandatory = $true)][string]$OriginalOutputDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw 'An exact source checkout is required.' }
Set-Location -LiteralPath $repo
$output = [IO.Path]::GetFullPath($OutputDirectory)
$original = [IO.Path]::GetFullPath($OriginalOutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh task-owned supplemental output directory.' }
if ($output.Equals($repo, [StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Supplemental outputs must be outside the source checkout.' }
if ($output.Equals($original, [StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($original + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Supplemental outputs must be separate from original native evidence.' }
New-Item -ItemType Directory -Path $output | Out-Null
$diagnostics = Join-Path $output 'diagnostics'
New-Item -ItemType Directory -Path $diagnostics | Out-Null
$result = [ordered]@{
    schemaVersion = 1; status = 'NOT_RUN'; stage = 'source'; sourceCommit = $ExpectedCommit
    scope = 'Separate normal permission retention two in Debug and Release; original owning113 and native eight/GUI/authority acceptance remain separate.'
    commands = @(); configurations = [ordered]@{ Debug = $null; Release = $null }
    buildTaskSHA256 = $null; avaloniaBuildTaskSHA256 = $null; originalTaskBodiesAfterUnchanged = $false; sourceAfterUnchanged = $false
    actualAssertionInvocationCount = $null; failureType = $null
    resourceQualification = 'Within the unchanged Windows job60 minute deadline; original helper has8MiB post-run per-log bound, not transient memory/disk enforcement. No native GUI/profile/provider operation here.'
}
function Write-Json([string]$Path, $Value) {
    $text = ConvertTo-Json -InputObject $Value -Depth 100
    [IO.File]::WriteAllText($Path, $text + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
function Save-Result { Write-Json (Join-Path $diagnostics 'result.json') $result }
function Invoke-DotNet([string]$Name, [string[]]$Arguments) {
    $started = [DateTime]::UtcNow
    $log = Join-Path $diagnostics ($Name + '.log')
    $lines = @(& dotnet @Arguments 2>&1 | Tee-Object -FilePath $log)
    $code = $LASTEXITCODE
    $lines | Out-Host
    $result.commands += [ordered]@{ name = $Name; argv = $Arguments; exitCode = $code; startedUTC = $started.ToString('o'); endedUTC = [DateTime]::UtcNow.ToString('o'); log = [IO.Path]::GetFileName($log) }
    Save-Result
    if ((Get-Item -LiteralPath $log).Length -gt 8MB) { throw "$Name exceeded the declared 8MiB log bound." }
    if ($code -ne 0) { throw "$Name failed with exit code $code; original log retained." }
    return (($lines | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine)
}
function Get-TreeSnapshot {
    $rows = @()
    $entries = @(& git -c core.quotepath=false ls-tree -r HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Source tree cannot be read.' }
    foreach ($entry in $entries) {
        if ($entry -notmatch '^([0-9]{6}) (blob|commit) ([0-9a-f]{40})\t(.+)$') { throw 'Unexpected source tree entry.' }
        $mode = $Matches[1]; $type = $Matches[2]; $blob = $Matches[3]; $path = $Matches[4]
        if ($type -eq 'commit') {
            $actual = (& git -C $path rev-parse HEAD).Trim()
            if ($LASTEXITCODE -ne 0 -or $actual -cne $blob) { throw "Pinned source gitlink mismatch: $path" }
            if (@(& git -C $path status --porcelain).Count -ne 0) { throw "Gitlink has changes: $path" }
            $rows += [ordered]@{ path = $path; mode = $mode; gitBlob = $blob; kind = 'gitlink'; actualCommit = $actual }
            foreach ($leaf in @(& git -C $path -c core.quotepath=false ls-tree -r HEAD)) {
                if ($leaf -notmatch '^([0-9]{6}) blob ([0-9a-f]{40})\t(.+)$') { throw 'Unresolved nested gitlink or dependency tree entry.' }
                $leafMode = $Matches[1]; $leafBlob = $Matches[2]; $leafPath = $Matches[3]; $physical = Join-Path $repo ($path + '/' + $leafPath)
                $item = Get-Item -LiteralPath $physical -Force
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked dependency body is unsupported.' }
                $rows += [ordered]@{ path = $path + '/' + $leafPath; mode = $leafMode; gitBlob = $leafBlob; kind = 'dependency-body'; bytes = $item.Length; sha256 = (Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash.ToLowerInvariant() }
            }
        } else {
            $physical = Join-Path $repo $path; $item = Get-Item -LiteralPath $physical -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Linked source body is unsupported: $path" }
            $rows += [ordered]@{ path = $path; mode = $mode; gitBlob = $blob; kind = 'source-body'; bytes = $item.Length; sha256 = (Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
    }
    return $rows
}
function Get-FileCatalog([string]$Root, [switch]$Exclusive) {
    $rows = @()
    foreach ($item in @(Get-ChildItem -LiteralPath $Root -Recurse -Force | Sort-Object FullName)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'A task output contains a reparse point.' }
        if ($item.PSIsContainer) { continue }
        if ($Exclusive) {
            $stream = [IO.File]::Open($item.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
            finally { $stream.Dispose() }
        } else { $digest = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        $rows += [ordered]@{ path = [IO.Path]::GetRelativePath($Root, $item.FullName).Replace('\', '/'); bytes = $item.Length; sha256 = $digest }
    }
    return $rows
}
function Same-Catalog($Left, $Right) {
    return (ConvertTo-Json -InputObject @($Left) -Depth 100 -Compress) -ceq (ConvertTo-Json -InputObject @($Right) -Depth 100 -Compress)
}
$before = $null
$cuiTasks = $null
$avaloniaTasks = $null
$failed = $false
try {
    if (-not $IsWindows) { throw 'The reviewed receiving runner is Windows.' }
    if ((& git rev-parse HEAD).Trim() -cne $ExpectedCommit) { throw 'The exact selected source changed.' }
    if (@(& git status --porcelain).Count -ne 0) { throw 'Source checkout must be clean.' }
    $before = @(Get-TreeSnapshot)
    Write-Json (Join-Path $diagnostics 'source-before.json') $before
    foreach ($pair in @(@('DOTNET_CLI_HOME','cli'),@('NUGET_HTTP_CACHE_PATH','http'),@('NUGET_PLUGINS_CACHE_PATH','plugins'),@('TMP','tmp'),@('TEMP','tmp'))) {
        $privatePath = Join-Path $output $pair[1]; New-Item -ItemType Directory -Path $privatePath -Force | Out-Null
        [Environment]::SetEnvironmentVariable($pair[0], $privatePath)
    }
    # Reuse only this same completed workflow step's ordinary package cache.
    $env:NUGET_PACKAGES = Join-Path $original 'nuget'
    if ((Invoke-DotNet 'sdk-version' @('--version')).Trim() -cne '10.0.401') { throw 'The normal original SDK is required.' }
    $sourceResult = Get-Content -LiteralPath (Join-Path $original 'diagnostics/result.json') -Raw | ConvertFrom-Json
    if ($sourceResult.sourceCommit -cne $ExpectedCommit -or -not $sourceResult.sourceAfterUnchanged) { throw 'Original source-built task custody must bind this exact unchanged checkout.' }
    $taskProperties = Get-Content -LiteralPath (Join-Path $original 'diagnostics/task-target-properties.log') -Raw | ConvertFrom-Json
    $cuiTasks = [IO.Path]::GetFullPath([string]$taskProperties.Properties.TargetPath)
    $avaloniaTasks = [IO.Path]::GetFullPath((Get-Content -LiteralPath (Join-Path $original 'diagnostics/avalonia-task-target-property.log') -Raw).Trim())
    $originalArtifacts = [IO.Path]::GetFullPath((Join-Path $original 'artifacts'))
    foreach ($task in @($cuiTasks, $avaloniaTasks)) {
        if (-not $task.StartsWith($originalArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $task -PathType Leaf)) { throw 'Use only the actual evaluated source-built tasks from this same original run.' }
        if (((Get-Item -LiteralPath $task -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'A task DLL must be a regular original file.' }
    }
    $result.buildTaskSHA256 = (Get-FileHash -LiteralPath $cuiTasks -Algorithm SHA256).Hash.ToLowerInvariant()
    $result.avaloniaBuildTaskSHA256 = (Get-FileHash -LiteralPath $avaloniaTasks -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($result.buildTaskSHA256 -cne $sourceResult.buildTaskSHA256 -or $result.avaloniaBuildTaskSHA256 -cne $sourceResult.avaloniaBuildTaskSHA256) { throw 'The two original source-built task bodies changed.' }
    $project = '9to1 Workspace/Home/RetentionTests/HavenOS.Home.PermissionRetention.Tests.csproj'
    $artifacts = Join-Path $output 'artifacts'
    $common = @("-p:CuiBuildTasksLocation=$cuiTasks", "-p:AvaloniaBuildTasksLocation=$avaloniaTasks", '--artifacts-path', $artifacts, '-p:UseSharedCompilation=false', '--disable-build-servers', '-m:1', '-nodeReuse:false')
    $expectedNames = @(
        'HavenOS.Home.Tests.HomePermissionSnapshotRetentionTests.Snapshot_without_expired_grants_preserves_actual_bytes_and_record_revisions_after_reopen',
        'HavenOS.Home.Tests.HomePermissionSnapshotRetentionTests.Snapshot_expiration_persists_once_and_reopened_snapshot_preserves_same_expired_audit'
    )
    foreach ($configuration in @('Debug', 'Release')) {
        $entry = [ordered]@{ status = 'NOT_RUN'; stage = 'restore'; originalNames = $expectedNames; cases = $null; runtimeBeforeAfterEqual = $false; assemblySHA256 = $null; errorType = $null }
        $result.configurations[$configuration] = $entry
        try {
            $result.stage = "$configuration-normal-two"
            [void](Invoke-DotNet "$configuration-restore" (@('restore', $project, '--configfile', 'NuGet.Config', '--disable-parallel', "-p:Configuration=$configuration") + $common))
            $entry.stage = 'build'
            [void](Invoke-DotNet "$configuration-build" (@('build', $project, '--no-restore', '-c', $configuration) + $common))
            $target = (Invoke-DotNet "$configuration-target" @('msbuild', $project, "-p:Configuration=$configuration", '-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$artifacts", "-p:CuiBuildTasksLocation=$cuiTasks", "-p:AvaloniaBuildTasksLocation=$avaloniaTasks", '-getProperty:TargetPath')).Trim()
            if (-not [IO.Path]::IsPathFullyQualified($target) -or -not $target.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $target -PathType Leaf)) { throw 'The actual supplemental target must belong to its own current artifact tree.' }
            $entry.assemblySHA256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
            $runtimeRoot = [IO.Path]::GetDirectoryName($target)
            $runtimeBefore = @(Get-FileCatalog $runtimeRoot -Exclusive)
            Write-Json (Join-Path $diagnostics "$configuration-runtime-before.json") $runtimeBefore
            $entry.stage = 'discovery'
            $discovery = Invoke-DotNet "$configuration-discovery" (@('test', $project, '--no-build', '--no-restore', '-c', $configuration, '--list-tests') + $common)
            $names = @($discovery -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_.StartsWith('HavenOS.Home.Tests.', [StringComparison]::Ordinal) })
            Write-Json (Join-Path $diagnostics "$configuration-discovery.json") $names
            if ($names.Count -ne 2) { throw 'The standalone original supplemental project must discover exactly two cases.' }
            foreach ($name in $expectedNames) { if (@($names | Where-Object { $_ -ceq $name }).Count -ne 1) { throw 'A supplemental discovery case is missing or duplicated.' } }
            $entry.stage = 'test'
            $resultsDirectory = Join-Path $diagnostics "$configuration-test-results"
            $testError = $null
            try { [void](Invoke-DotNet "$configuration-test" (@('test', $project, '--no-build', '--no-restore', '-c', $configuration, '--logger', "trx;LogFileName=permission-retention-$configuration.trx", '--results-directory', $resultsDirectory) + $common)) }
            catch { $testError = $_.Exception }
            $trxPath = Join-Path $resultsDirectory "permission-retention-$configuration.trx"
            [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
            $counter = $trx.SelectSingleNode("//*[local-name()='Counters']")
            $cases = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
            $entry.cases = [ordered]@{ total = [int]$counter.total; executed = [int]$counter.executed; passed = [int]$counter.passed; failed = [int]$counter.failed; notExecuted = [int]$counter.notExecuted; names = @($cases | ForEach-Object { $_.testName }); filter = $null; assertions = $null }
            $runtimeAfter = @(Get-FileCatalog $runtimeRoot -Exclusive)
            Write-Json (Join-Path $diagnostics "$configuration-runtime-after.json") $runtimeAfter
            $entry.runtimeBeforeAfterEqual = Same-Catalog $runtimeBefore $runtimeAfter
            if (-not $entry.runtimeBeforeAfterEqual) { throw 'Actual supplemental runtime bodies changed across discovery and tests.' }
            if ($testError) { throw $testError }
            if ($counter.total -ne 2 -or $counter.executed -ne 2 -or $counter.passed -ne 2 -or $counter.failed -ne 0 -or $counter.notExecuted -ne 0 -or $cases.Count -ne 2) { throw 'Both supplemental cases must pass without skips.' }
            foreach ($name in $expectedNames) { if (@($cases | Where-Object { $_.testName -ceq $name -and $_.outcome -ceq 'Passed' }).Count -ne 1) { throw 'A supplemental result is missing, duplicated or not passed.' } }
            $entry.status = 'PASS_SCOPED_TWO'; $entry.stage = 'complete'
        } catch { $entry.status = 'FAIL_OR_INCOMPLETE'; $entry.errorType = $_.Exception.GetType().FullName; $failed = $true }
        Save-Result
    }
    if (-not $failed) { $result.status = 'PASS_SEPARATE_TWO_DEBUG_AND_RELEASE'; $result.stage = 'complete' }
} catch { $result.status = 'FAIL_OR_INCOMPLETE'; $result.failureType = $_.Exception.GetType().FullName; $failed = $true }
finally {
    try {
        $after = @(Get-TreeSnapshot)
        Write-Json (Join-Path $diagnostics 'source-after.json') $after
        if ($null -ne $before) { $result.sourceAfterUnchanged = Same-Catalog $before $after }
        if (-not $result.sourceAfterUnchanged -or @(& git status --porcelain).Count -ne 0) { throw 'Actual source changed during supplemental validation.' }
        if ($cuiTasks -and $avaloniaTasks -and $result.buildTaskSHA256 -and $result.avaloniaBuildTaskSHA256) {
            $result.originalTaskBodiesAfterUnchanged = ((Get-FileHash -LiteralPath $cuiTasks -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $result.buildTaskSHA256) -and ((Get-FileHash -LiteralPath $avaloniaTasks -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $result.avaloniaBuildTaskSHA256)
            if (-not $result.originalTaskBodiesAfterUnchanged) { throw 'Original source-built task bodies changed during supplemental validation.' }
        }
    } catch { $result.status = 'FAIL_OR_INCOMPLETE'; $result.failureType = $_.Exception.GetType().FullName; $failed = $true }
    if ($failed) { $result.status = 'FAIL_OR_INCOMPLETE' }
    Save-Result
}
if ($failed) { Write-Host 'Standalone permission retention validation failed or is incomplete; original diagnostics retained.'; exit 1 }
exit 0
