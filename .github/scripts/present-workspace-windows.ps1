[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (& git rev-parse --show-toplevel).Trim()
Set-Location -LiteralPath $repo
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'A fresh owned output is required.' }
if ($output.Equals($repo, [StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Outputs must be outside source.' }
$diagnostics = Join-Path $output 'diagnostics'; New-Item -ItemType Directory -Path $diagnostics | Out-Null
$catalog = Get-Content -LiteralPath '.github/validation/present-workspace-windows.json' -Raw | ConvertFrom-Json
$result = [ordered]@{
    schemaVersion = 1; status = 'NOT_RUN'; stage = 'source'; workflowCommit = $ExpectedCommit; runId = $env:GITHUB_RUN_ID; runAttempt = $env:GITHUB_RUN_ATTEMPT
    sourceBasis = $catalog.sourceBasis; commands = @(); managedControls = @(); sourceAfterUnchanged = $false; package = $null; nativeUiExitCode = $null
    qualification = 'Provisional current-source Windows release package, original owning15/shared7 and bounded native UI; SDK-present disposable runner, no installedHome/signing/cleanPC/donor/Android/full app acceptance.'
    originalNegative = $catalog.retainedOriginalNegative; acceptanceVerified = $false; fullAppAcceptance = 'NOT_RUN'; ownerAdoption = 'UNACCEPTED'
}
function Write-Json([string]$Path, $Value) { [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 100) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) }
function Save-Result { Write-Json (Join-Path $diagnostics 'result.json') $result }
function Invoke-DotNet([string]$Name, [string[]]$Arguments, [switch]$RetainFailure) {
    $log = Join-Path $diagnostics ($Name + '.log'); $started = [DateTime]::UtcNow
    $lines = @(& dotnet @Arguments 2>&1 | Tee-Object -FilePath $log); $code = $LASTEXITCODE; $lines | Out-Host
    $result.commands += [ordered]@{ name = $Name; argv = $Arguments; exitCode = $code; startedUTC = $started.ToString('o'); endedUTC = [DateTime]::UtcNow.ToString('o'); log = [IO.Path]::GetFileName($log) }; Save-Result
    if ((Get-Item -LiteralPath $log).Length -gt 8MB) { throw 'Original command exceeded the declared post-completion 8MiB log bound.' }
    if ($code -ne 0 -and -not $RetainFailure) { throw 'Original command failed; exact retained command exit/log is the cause.' }
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
function Run-OriginalCases([string]$Label, [string]$Project, [int]$ExpectedCases, [string]$Filter) {
    $control = [ordered]@{ cohort = $Label; project = $Project; filter = $Filter; requiredCases = $ExpectedCases; status = 'NOT_RUN'; total = 0; executed = 0; passed = 0; failed = 0; skipped = 0; names = @(); assertionCount = $null }
    try {
        [void](Invoke-DotNet ($Label+'-restore') (@('restore',$Project,'--configfile','NuGet.Config','--disable-parallel','-p:Configuration=Release',$taskFlag) + $flags))
        [void](Invoke-DotNet ($Label+'-build-release') (@('build',$Project,'--no-restore','-c','Release',$taskFlag) + $flags))
        $assembly = (Invoke-DotNet ($Label+'-target-path') @('msbuild',$Project,'-p:Configuration=Release','-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$artifacts",$taskFlag,'-getProperty:TargetPath')).Trim()
        if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Original owning assembly is absent.' }
        $runtime = [IO.Path]::GetDirectoryName($assembly); $runtimeBefore = @(Get-FileCatalog $runtime); Write-Json (Join-Path $diagnostics ($Label+'-runtime-before.json')) $runtimeBefore
        $control.assemblySHA256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()
        $selection = @(); if (-not [string]::IsNullOrWhiteSpace($Filter)) { $selection = @('--filter',$Filter) }
        $discovery = Invoke-DotNet ($Label+'-discovery') (@('test',$Project,'--no-build','--no-restore','-c','Release','--list-tests',$taskFlag) + $selection + $flags)
        $namespacePrefix = if ($Label -ceq 'owning') { 'HavenOS.Apps.Present.Tests.' } else { 'Haven.Desktop.Tests.PresentPageTests.' }
        $names = @($discovery -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_.StartsWith($namespacePrefix,[StringComparison]::Ordinal) })
        Write-Json (Join-Path $diagnostics ($Label+'-discovery.json')) ([ordered]@{ total = $names.Count; names = $names; filter = $Filter })
        if ($names.Count -ne $ExpectedCases) { throw 'Original normal discovery differs from exact required cases.' }
        $tests = Join-Path $diagnostics ($Label+'-test-results'); New-Item -ItemType Directory -Path $tests | Out-Null
        [void](Invoke-DotNet ($Label+'-test-release') (@('test',$Project,'--no-build','--no-restore','-c','Release','--logger',("trx;LogFileName="+$Label+'.trx'),'--results-directory',$tests,$taskFlag) + $selection + $flags) -RetainFailure)
        [xml]$trx = Get-Content -LiteralPath (Join-Path $tests ($Label+'.trx')) -Raw
        $ns = [Xml.XmlNamespaceManager]::new($trx.NameTable); $ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $counter = $trx.SelectSingleNode('//t:Counters',$ns); $cases = @($trx.SelectNodes('//t:UnitTestResult',$ns))
        $control.total = [int]$counter.total; $control.executed = [int]$counter.executed; $control.passed = [int]$counter.passed; $control.failed = [int]$counter.failed; $control.skipped = [int]$counter.notExecuted; $control.names = @($cases | ForEach-Object { $_.testName })
        $runtimeAfter = @(Get-FileCatalog $runtime -Exclusive); Write-Json (Join-Path $diagnostics ($Label+'-runtime-after.json')) $runtimeAfter
        if (-not (Same-Catalog $runtimeBefore $runtimeAfter)) { throw 'Original runtime changed across discovery/run.' }
        if ($control.total -ne $ExpectedCases -or $control.executed -ne $ExpectedCases -or $control.passed -ne $ExpectedCases -or $control.failed -ne 0 -or $control.skipped -ne 0 -or $cases.Count -ne $ExpectedCases) { throw 'All original required controls must execute and pass without skips.' }
        if ($Label -ceq 'owning' -and (@($cases | Where-Object { $_.testName -like '*PresentWorkspaceCompositionTests.*' }).Count -ne 2 -or @($cases | Where-Object { $_.testName -like '*PresentThemeBootstrapTests.Existing_present_route_initializes_and_renders_in_each_appearance*' }).Count -ne 8)) { throw 'Two mounted regressions or eight original appearance controls are absent.' }
        if (@($result.commands | Where-Object { $_.name -ceq ($Label+'-test-release') -and $_.exitCode -ne 0 }).Count -gt 0) { throw 'Original owning process returned a failed outcome despite counters.' }
        $control.status = 'PASS'
    } catch { $control.status = 'FAIL_OR_INCOMPLETE'; $control.failureType = $_.Exception.GetType().FullName; $control.scriptLine = $_.InvocationInfo.ScriptLineNumber; $control.rawExceptionText = 'WITHHELD' }
    finally { $result.managedControls += $control; Save-Result }
}
$before = $null
try {
    if (-not [OperatingSystem]::IsWindows() -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) { throw 'Actual Windows x64 is required.' }
    if ((& git rev-parse HEAD).Trim() -cne $ExpectedCommit) { throw 'Exact selected producer source is required.' }
    & git merge-base --is-ancestor $catalog.sourceBasis HEAD; if ($LASTEXITCODE -ne 0) { throw 'Wrong source lineage.' }
    foreach ($path in @(& git diff --name-only $catalog.sourceBasis HEAD)) { if ($catalog.harnessPaths -cnotcontains $path) { throw 'Undeclared change after the exact native proposal.' } }
    foreach ($pin in $catalog.sourcePins) { $physical = Join-Path $repo $pin.path; if ((Get-Item -LiteralPath $physical).Length -ne $pin.bytes -or (Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pin.sha256) { throw 'Actual source body pin mismatch.' } }
    foreach ($link in $catalog.requiredGitlinks) { if ((& git -C $link.path rev-parse HEAD).Trim() -cne $link.commit -or $LASTEXITCODE -ne 0) { throw 'Actual two-link source dependency differs.' } }
    if (@(& git status --porcelain).Count -ne 0) { throw 'Actual source checkout is not clean.' }
    $available = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($output)).AvailableFreeSpace; $result.initialOutputDriveFreeBytes = $available
    if ($available -lt 8GB) { throw 'At least declared 8GiB disposable output-drive capacity is required.' }
    $before = @(Get-TreeSnapshot); Write-Json (Join-Path $diagnostics 'source-before.json') $before
    foreach ($pair in @(@('DOTNET_CLI_HOME','cli'),@('NUGET_PACKAGES','nuget'),@('NUGET_HTTP_CACHE_PATH','http'),@('NUGET_PLUGINS_CACHE_PATH','plugins'),@('TMP','tmp'),@('TEMP','tmp'))) { $privatePath = Join-Path $output $pair[1]; New-Item -ItemType Directory -Path $privatePath -Force | Out-Null; [Environment]::SetEnvironmentVariable($pair[0],$privatePath) }
    if ((Invoke-DotNet 'sdk-version' @('--version')).Trim() -cne '10.0.401') { throw 'Repository SDK identity differs.' }
    $artifacts = Join-Path $output 'artifacts'; $flags = @('--artifacts-path',$artifacts,'-p:UseSharedCompilation=false','--disable-build-servers','-m:1','-nodeReuse:false')
    $tasksProject = 'framework/CUI/Compiler/CakeOS.Cui.Build.Tasks/CakeOS.Cui.Build.Tasks.csproj'; $result.stage = 'current-source-tasks'
    [void](Invoke-DotNet 'cui-tasks-release' (@('build',$tasksProject,'-c','Release','--nologo') + $flags))
    $taskProperties = Invoke-DotNet 'tasks-properties' @('msbuild',$tasksProject,'-p:Configuration=Release','-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$artifacts",'-getProperty:TargetPath,TargetFramework') | ConvertFrom-Json
    $tasks = $taskProperties.Properties.TargetPath; if (-not (Test-Path -LiteralPath $tasks -PathType Leaf)) { throw 'Actual source-built Task DLL is absent.' }
    $result.taskAssemblySHA256 = (Get-FileHash -LiteralPath $tasks -Algorithm SHA256).Hash.ToLowerInvariant(); $taskFlag = "-p:CuiBuildTasksLocation=$tasks"
    $result.stage = 'ordinary-owning15-and-shared7'
    Run-OriginalCases 'owning' $catalog.owningProject ([int]$catalog.owningCases) ''
    Run-OriginalCases 'shared' $catalog.sharedProject ([int]$catalog.sharedCases) $catalog.sharedFilter
    $result.stage = 'new-native-release-package'
    $nativeProject = '9to1 Workspace/Present/HavenOS.Present.csproj'
    [void](Invoke-DotNet 'native-present-release' (@('build',$nativeProject,'-c','Release','-f','net10.0',$taskFlag) + $flags))
    $publish = Join-Path $output 'publish'
    [void](Invoke-DotNet 'publish-release-win-x64' (@('publish',$nativeProject,'-c','Release','-f','net10.0','-r','win-x64','--self-contained','true','-p:PublishSingleFile=false','-p:PublishReadyToRun=false','-p:DebugType=portable',$taskFlag,'-o',$publish,'--nologo') + $flags))
    foreach ($required in @('HavenOS.Present.exe','HavenOS.Present.deps.json','HavenOS.Present.runtimeconfig.json')) { if (-not (Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) { throw 'Actual original project package sidecar is absent.' } }
    $runtime = @(Get-FileCatalog $publish); [long]$uncompressed = 0; foreach ($file in $runtime) { $uncompressed += $file.bytes }
    $publication = Join-Path $output 'public-artifact'; New-Item -ItemType Directory -Path $publication | Out-Null
    $zip = Join-Path $publication 'present-win-x64-unaccepted.zip'; [IO.Compression.ZipFile]::CreateFromDirectory($publish,$zip)
    $zipItem = Get-Item -LiteralPath $zip; $zipSHA = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $result.package = [ordered]@{ file = 'present-win-x64-unaccepted.zip'; bytes = $zipItem.Length; sha256 = $zipSHA; fileCount = $runtime.Count; uncompressedBytes = $uncompressed; authenticodeStatus = (Get-AuthenticodeSignature -LiteralPath (Join-Path $publish $catalog.executable)).Status.ToString() }
    $manifest = [ordered]@{ schemaVersion = 1; target = 'present'; workflowCommit = $ExpectedCommit; runId = $env:GITHUB_RUN_ID; runAttempt = $env:GITHUB_RUN_ATTEMPT; package = $result.package; files = $runtime; nativeSourceProposal = $catalog.sourceBasis; qualification = $result.qualification }
    $manifestPath = Join-Path $publication 'present-producer-manifest.json'; Write-Json $manifestPath $manifest
    Write-Json (Join-Path $publication 'seal.json') ([ordered]@{ sourceCommit = $ExpectedCommit; runId = $env:GITHUB_RUN_ID; runAttempt = $env:GITHUB_RUN_ATTEMPT; manifestSHA256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant(); archiveSHA256 = $zipSHA; archiveBytes = $zipItem.Length })
    if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value 'package_ready=true' }
    $parts = Join-Path $output 'public-parts'; New-Item -ItemType Directory -Path $parts | Out-Null
    $partLimit = [long]$catalog.transport.rawPartMaximumBytes; $partCount = [int][Math]::Ceiling($zipItem.Length / $partLimit)
    if ($partCount -gt [int]$catalog.transport.maximumParts) { throw 'New package exceeds declared raw transport bound; whole sealed archive remains retained.' }
    $partRows = @(); $sourceStream = [IO.File]::OpenRead($zip)
    try {
        for ($number = 1; $number -le $partCount; $number++) {
            $directory = Join-Path $parts ('part{0:d2}' -f $number); New-Item -ItemType Directory -Path $directory | Out-Null
            $partPath = Join-Path $directory 'present-win-x64.zip.part'; $stream = [IO.File]::Create($partPath)
            try { [long]$remaining = [Math]::Min($partLimit,$sourceStream.Length-$sourceStream.Position); $buffer = [byte[]]::new(65536); while ($remaining -gt 0) { $read = $sourceStream.Read($buffer,0,[int][Math]::Min($buffer.Length,$remaining)); if ($read -le 0) { throw 'Unexpected original ZIP EOF.' }; $stream.Write($buffer,0,$read); $remaining -= $read } } finally { $stream.Dispose() }
            $partRows += [ordered]@{ part = $number; path = ('part{0:d2}/present-win-x64.zip.part' -f $number); bytes = (Get-Item -LiteralPath $partPath).Length; sha256 = (Get-FileHash -LiteralPath $partPath -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
    } finally { $sourceStream.Dispose() }
    $reassembly = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256); [long]$counted = 0
    try { foreach ($part in $partRows) { $stream = [IO.File]::OpenRead((Join-Path $parts $part.path)); try { $buffer = [byte[]]::new(65536); while (($read = $stream.Read($buffer,0,$buffer.Length)) -gt 0) { $reassembly.AppendData($buffer,0,$read); $counted += $read } } finally { $stream.Dispose() } }; $joinedSHA = [Convert]::ToHexString($reassembly.GetHashAndReset()).ToLowerInvariant() } finally { $reassembly.Dispose() }
    if ($counted -ne $zipItem.Length -or $joinedSHA -cne $zipSHA) { throw 'Ordered raw part bytes differ from the original archive.' }
    $partsManifest = [ordered]@{ sourceCommit = $ExpectedCommit; runId = $env:GITHUB_RUN_ID; runAttempt = $env:GITHUB_RUN_ATTEMPT; originalArchiveBytes = $zipItem.Length; originalArchiveSHA256 = $zipSHA; orderedReassemblyVerified = $true; parts = $partRows }
    foreach ($directory in @(Get-ChildItem -LiteralPath $parts -Directory)) { Write-Json (Join-Path $directory.FullName 'parts-manifest.json') $partsManifest; Copy-Item -LiteralPath $manifestPath,(Join-Path $publication 'seal.json') -Destination $directory.FullName }
    if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "part_count=$partCount"; Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value 'parts_ready=true' }
    $result.stage = 'actual-new-package-native-ui'
    $nativeOutput = Join-Path $output 'native-ui'
    & powershell.exe -NoProfile -NonInteractive -File (Join-Path $repo '.github/scripts/present-workspace-native-ui-controls.ps1') -PackageInput $publication -ObservationInput $publication -OutputDirectory $nativeOutput -ExpectedCommit $ExpectedCommit -ExpectedRunId $env:GITHUB_RUN_ID -ExpectedRunAttempt $env:GITHUB_RUN_ATTEMPT
    $result.nativeUiExitCode = $LASTEXITCODE
    $runtimeAfter = @(Get-FileCatalog $publish -Exclusive); Write-Json (Join-Path $diagnostics 'producer-runtime-after.json') $runtimeAfter
    if (-not (Same-Catalog $runtime $runtimeAfter)) { throw 'Original published runtime changed during native observation.' }
    if ($result.nativeUiExitCode -ne 0) { throw 'Actual new package native UI criteria failed; safe first original observation retained.' }
    $ui = Get-Content -LiteralPath (Join-Path $nativeOutput 'result.json') -Raw | ConvertFrom-Json
    if ($ui.status -cne 'BOUNDED_PACKAGED_NATIVE_UI_CONTROLS_PASS_UNACCEPTED' -or $ui.forcedCleanup -or @($ui.launches).Count -ne 2 -or @($ui.launches | Where-Object { -not $_.exited -or $_.exitCode -ne 0 }).Count -ne 0) { throw 'Native UI did not satisfy unchanged normal-exit criteria.' }
    if ($result.managedControls.Count -ne 2 -or @($result.managedControls | Where-Object { $_.status -cne 'PASS' }).Count -ne 0) { throw 'Both complete normal owning and preserved shared controls must pass.' }
    $result.status = 'PASS_BOUNDED_CURRENT_SOURCE_PACKAGE_AND_NATIVE_UI_UNACCEPTED'
} catch { $result.status = 'FAIL_OR_INCOMPLETE'; $result.failure = [ordered]@{ type = $_.Exception.GetType().FullName; stage = $result.stage; scriptLine = $_.InvocationInfo.ScriptLineNumber; rawExceptionText = 'WITHHELD' } }
finally {
    try { $after = @(Get-TreeSnapshot); Write-Json (Join-Path $diagnostics 'source-after.json') $after; if ($null -ne $before) { $result.sourceAfterUnchanged = Same-Catalog $before $after }; if (-not $result.sourceAfterUnchanged -or @(& git status --porcelain).Count -ne 0) { throw 'Current source custody was not preserved.' } } catch { $result.status = 'FAIL_OR_INCOMPLETE'; $result.finalCustodyFailureType = $_.Exception.GetType().FullName }
    try { Save-Result } catch { $result.status = 'FAIL_OR_INCOMPLETE'; Write-Error 'Final safe producer evidence write failed.' -ErrorAction Continue }
}
if ($result.status -cne 'PASS_BOUNDED_CURRENT_SOURCE_PACKAGE_AND_NATIVE_UI_UNACCEPTED' -or -not $result.sourceAfterUnchanged) { exit 1 }
exit 0
