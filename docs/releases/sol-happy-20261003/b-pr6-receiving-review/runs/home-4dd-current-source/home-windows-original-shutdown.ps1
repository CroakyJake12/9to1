[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (& git rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0) { throw 'An exact source checkout is required.' }
Set-Location -LiteralPath $repo
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh task-owned output directory.' }
if ($output.Equals($repo, [StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build and profile outputs must be outside the source checkout.'
}
New-Item -ItemType Directory -Path $output | Out-Null
$diagnostics = Join-Path $output 'diagnostics'
New-Item -ItemType Directory -Path $diagnostics | Out-Null
$catalog = Get-Content -LiteralPath '.github/validation/home-windows-original-shutdown.json' -Raw | ConvertFrom-Json
$result = [ordered]@{
    schemaVersion = 1; status = 'NOT_RUN'; stage = 'source'; sourceCommit = $ExpectedCommit
    sourceBasis = $catalog.sourceBasis; platform = 'Windows/win-x64'; commands = @(); nativeProcesses = @()
    managedCases = $null; buildTaskSHA256 = $null; avaloniaBuildTaskSHA256 = $null; package = $null; sourceAfterUnchanged = $false
    acceptedNativeShutdownScope = $false; processSignals = @(); forcedCleanup = $false
    originalForcedFailure = $catalog.originalForcedFailure
    qualification = 'Original owning six tests and current unaccepted Home package/visible-window explicit quit only; no installed identity, signing, clean-PC, provider, all-theme or full Home/release acceptance.'
    ordinaryWindowClose = 'NOT_RUN: Close intentionally keeps Core alive. No original external quit/reopen route exists once the sole HWND is gone; old forced-1 remains retained.'
    nativePersistenceMutation = 'NOT_RUN: current host exposes no configured writer. Two fresh processes use the original OS LocalApplicationData/9to1/Home route; scoped catalog equality is read-only cold-open observation.'
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
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class HomeOriginalNativeInput {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [StructLayout(LayoutKind.Explicit, Size=40)] public struct Input {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort VirtualKey;
        [FieldOffset(10)] public ushort ScanCode;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(16)] public uint Time;
        [FieldOffset(24)] public UIntPtr ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    private static Input Key(ushort key, bool up) => new Input { Type=1, VirtualKey=key, Flags=up ? 2u : 0u };
    public static uint QuitChord() => SendInput(6, new[] { Key(0x11,false), Key(0x10,false), Key(0x51,false), Key(0x51,true), Key(0x10,true), Key(0x11,true) }, Marshal.SizeOf<Input>());
    public static uint ReleaseChordKeys() => SendInput(3, new[] { Key(0x51,true), Key(0x10,true), Key(0x11,true) }, Marshal.SizeOf<Input>());
}
'@
function Invoke-OriginalNativeQuit([string]$Label, [string]$Exe) {
    $stdout = Join-Path $diagnostics ($Label + '-stdout.log'); $stderr = Join-Path $diagnostics ($Label + '-stderr.log')
    $observed = [ordered]@{ cohort = $Label; pid = $null; sessionId = $null; windowVisible = $false; ownHWND = $false; foregroundVerified = $false; inputEventsSent = 0; exited = $false; exitCode = $null; forcedCleanup = $false; processSignals = @(); outputDrained = $false; originalCuiRootReported = $false; phase = 'launch'; errorType = $null }
    $nativeProcess = $null
    try {
        $nativeProcess = Start-Process -FilePath $Exe -WorkingDirectory ([IO.Path]::GetDirectoryName($Exe)) -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $observed.pid = $nativeProcess.Id; $observed.sessionId = $nativeProcess.SessionId; $observed.startedUTC = [DateTime]::UtcNow.ToString('o')
        $observed.phase = 'visible-own-window'
        $until = [DateTime]::UtcNow.AddSeconds(45)
        $window = [IntPtr]::Zero
        do {
            $nativeProcess.Refresh()
            if ($nativeProcess.HasExited) { throw 'Original native process exited before its visible window appeared.' }
            $window = $nativeProcess.MainWindowHandle
            if ($window -ne [IntPtr]::Zero -and [HomeOriginalNativeInput]::IsWindowVisible($window)) { break }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $until)
        if ($window -eq [IntPtr]::Zero -or -not [HomeOriginalNativeInput]::IsWindowVisible($window)) { throw 'No actual visible original Home window was observed.' }
        [uint32]$ownerPid = 0
        [void][HomeOriginalNativeInput]::GetWindowThreadProcessId($window, [ref]$ownerPid)
        if ($ownerPid -ne $nativeProcess.Id) { throw 'Native HWND does not belong to this exact launched process.' }
        $observed.windowVisible = $true; $observed.ownHWND = $true; $observed.windowTitle = $nativeProcess.MainWindowTitle
        $observed.phase = 'own-window-focus'
        [void][HomeOriginalNativeInput]::ShowWindow($window, 9)
        [void][HomeOriginalNativeInput]::SetForegroundWindow($window)
        $until = [DateTime]::UtcNow.AddSeconds(5)
        while ([HomeOriginalNativeInput]::GetForegroundWindow() -ne $window -and [DateTime]::UtcNow -lt $until) { Start-Sleep -Milliseconds 100 }
        if ([HomeOriginalNativeInput]::GetForegroundWindow() -ne $window) { throw 'Actual own window foreground could not be verified; no keyboard event sent.' }
        $observed.foregroundVerified = $true
        foreach ($key in @(0x10,0x11,0x12)) { if (([HomeOriginalNativeInput]::GetAsyncKeyState($key) -band 0x8000) -ne 0) { throw 'Existing modifier key is held; do not alter unrelated desktop input.' } }
        $observed.phase = 'owned-input'
        if ([HomeOriginalNativeInput]::GetForegroundWindow() -ne $window -or -not [HomeOriginalNativeInput]::IsWindowVisible($window)) { throw 'Own visible foreground changed before original input; no key event sent.' }
        try { $observed.inputEventsSent = [HomeOriginalNativeInput]::QuitChord() }
        finally { $observed.releaseEventsSent = [HomeOriginalNativeInput]::ReleaseChordKeys() }
        if ($observed.releaseEventsSent -ne 3) { throw 'Win32 did not admit all owned modifier release events.' }
        if ($observed.inputEventsSent -ne 6) { throw 'Win32 did not admit all original Ctrl+Shift+Q key events.' }
        $observed.phase = 'original-process-exit'
        if (-not $nativeProcess.WaitForExit(45000)) { throw 'Original explicit shutdown did not exit within observation bound; no kill/timeout success is allowed.' }
        $nativeProcess.Refresh(); $observed.exited = $true; $observed.exitCode = $nativeProcess.ExitCode; $observed.endedUTC = [DateTime]::UtcNow.ToString('o')
        $observed.phase = 'bounded-original-output-drain'
        $drainDeadline = [DateTime]::UtcNow.AddSeconds(5)
        foreach ($log in @($stdout,$stderr)) {
            if ((Get-Item -LiteralPath $log).Length -gt 8MB) { throw 'Native output exceeded declared 8MiB bound.' }
            while ($true) {
                try { $stream = [IO.File]::Open($log, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None); $stream.Dispose(); break }
                catch [IO.IOException] { if ([DateTime]::UtcNow -ge $drainDeadline) { throw }; Start-Sleep -Milliseconds 100 }
            }
        }
        $observed.outputDrained = $true
        $observed.phase = 'original-cui-root-outcome'
        $text = Get-Content -LiteralPath $stdout -Raw
        $observed.originalCuiRootReported = $text -match '\[9-1 Home\] Root: .+ — CUI loaded into window'
        if ($observed.exitCode -ne 0) { throw 'Actual original process returned a nonzero shutdown outcome.' }
        if (-not $observed.originalCuiRootReported) { throw 'Original process did not report its canonical CUI root; a window title is insufficient.' }
        $observed.phase = 'complete'
        return $observed
    } catch {
        $observed.errorType = $_.Exception.GetType().Name
        if ($nativeProcess -and $nativeProcess.HasExited) { $observed.exited = $true; $observed.exitCode = $nativeProcess.ExitCode }
        throw
    } finally {
        $result.nativeProcesses += $observed
        Save-Result
        # Never force-kill or turn timeout into success. If still alive, retain that open family.
        if ($nativeProcess -and -not $nativeProcess.HasExited) { $result.openNativeProcess = $nativeProcess.Id; Save-Result }
        if ($nativeProcess) { $nativeProcess.Dispose() }
    }
}
$before = $null
try {
    if (-not [OperatingSystem]::IsWindows() -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) { throw 'Actual Windows x64 execution is required.' }
    if ((& git rev-parse HEAD).Trim() -cne $ExpectedCommit) { throw 'Execution source differs from exact selected commit.' }
    & git merge-base --is-ancestor $catalog.sourceBasis HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Execution is outside the frozen reviewed source lineage.' }
    foreach ($path in @(& git diff --name-only $catalog.sourceBasis HEAD)) {
        if ($catalog.proposalPaths -cnotcontains $path -and -not $path.StartsWith('docs/releases/sol-happy-20261003/', [StringComparison]::Ordinal)) { throw "Undeclared receiving change: $path" }
    }
    $result.runnerImage = $env:ImageVersion
    $result.osVersion = [Environment]::OSVersion.VersionString
    $available = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($output)).AvailableFreeSpace
    $result.initialOutputDriveFreeBytes = $available
    if ($available -lt 8GB) { throw 'The original native graph requires at least the declared 8GiB disposable output-drive capacity.' }
    foreach ($link in $catalog.requiredGitlinks) {
        if ((& git -C $link.path rev-parse HEAD).Trim() -cne $link.commit -or $LASTEXITCODE -ne 0) { throw 'Required current source gitlink is absent or differs.' }
    }
    foreach ($pin in $catalog.sourcePins) {
        if ((Get-FileHash -LiteralPath (Join-Path $repo $pin.path) -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pin.sha256) { throw "Current original source pin mismatch: $($pin.path)" }
    }
    if (@(& git status --porcelain).Count -ne 0) { throw 'Source checkout is not clean before invocation.' }
    $before = @(Get-TreeSnapshot); Write-Json (Join-Path $diagnostics 'source-before.json') $before
    foreach ($pair in @(@('DOTNET_CLI_HOME','cli'),@('NUGET_PACKAGES','nuget'),@('NUGET_HTTP_CACHE_PATH','http'),@('NUGET_PLUGINS_CACHE_PATH','plugins'),@('TMP','tmp'),@('TEMP','tmp'))) {
        $privatePath = Join-Path $output $pair[1]; New-Item -ItemType Directory -Path $privatePath -Force | Out-Null
        [Environment]::SetEnvironmentVariable($pair[0], $privatePath)
    }
    if ((Invoke-DotNet 'sdk-version' @('--version')).Trim() -cne '10.0.401') { throw 'Unexpected actual SDK.' }
    $artifacts = Join-Path $output 'artifacts'
    $flags = @('--artifacts-path',$artifacts,'-p:UseSharedCompilation=false','--disable-build-servers','-m:1','-nodeReuse:false')
    $tasksProject = 'framework/CUI/Compiler/CakeOS.Cui.Build.Tasks/CakeOS.Cui.Build.Tasks.csproj'
    $result.stage = 'build-current-tasks'
    [void](Invoke-DotNet 'cui-tasks-release' (@('build',$tasksProject,'-c','Release','--nologo') + $flags))
    $taskProperties = Invoke-DotNet 'task-target-properties' @('msbuild',$tasksProject,'-p:Configuration=Release','-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$artifacts",'-getProperty:TargetPath,TargetFramework') | ConvertFrom-Json
    $tasks = $taskProperties.Properties.TargetPath
    if (-not (Test-Path -LiteralPath $tasks -PathType Leaf)) { throw 'Actual source-built current Task DLL is missing.' }
    $result.buildTaskSHA256 = (Get-FileHash -LiteralPath $tasks -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskFlag = "-p:CuiBuildTasksLocation=$tasks"
    # The same ordinary dependency graph builds this original task before Fonts/Dialogs resources.
    # Bind its public maintained override to the source-evaluated owned artifacts path.
    $avaloniaTaskProject = 'framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'
    $avaloniaTask = (Invoke-DotNet 'avalonia-task-target-property' @('msbuild',$avaloniaTaskProject,'-p:Configuration=Release','-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$artifacts",'-getProperty:TargetPath')).Trim()
    if (-not [IO.Path]::IsPathFullyQualified($avaloniaTask) -or -not $avaloniaTask.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Original evaluated Avalonia Task path must be within owned artifacts.' }
    $flags += "-p:AvaloniaBuildTasksLocation=$avaloniaTask"
    $project = 'apps/Home/tests/AvaloniaHome.Tests/AvaloniaHome.Tests.csproj'
    $result.stage = 'ordinary-owning-six'
    [void](Invoke-DotNet 'owning-restore' (@('restore',$project,'--configfile','NuGet.Config','--disable-parallel','-p:Configuration=Release',$taskFlag) + $flags))
    [void](Invoke-DotNet 'owning-build-release' (@('build',$project,'--no-restore','-c','Release',$taskFlag) + $flags))
    if (-not (Test-Path -LiteralPath $avaloniaTask -PathType Leaf)) { throw 'The normal original dependency graph did not build its evaluated Avalonia Task DLL.' }
    $result.avaloniaBuildTaskSHA256 = (Get-FileHash -LiteralPath $avaloniaTask -Algorithm SHA256).Hash.ToLowerInvariant()
    $testProperties = Invoke-DotNet 'test-target-properties' @('msbuild',$project,'-p:Configuration=Release','-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$artifacts",$taskFlag,'-getProperty:TargetPath')
    $testAssembly = $testProperties.Trim()
    if (-not (Test-Path -LiteralPath $testAssembly -PathType Leaf)) { throw 'Actual original owning test assembly is missing.' }
    $testRuntimeRoot = [IO.Path]::GetDirectoryName($testAssembly)
    $testRuntimeBefore = @(Get-FileCatalog $testRuntimeRoot)
    Write-Json (Join-Path $diagnostics 'owning-runtime-before.json') $testRuntimeBefore
    $result.owningAssemblySHA256 = (Get-FileHash -LiteralPath $testAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
    $discovery = Invoke-DotNet 'owning-discovery' (@('test',$project,'--no-build','--no-restore','-c','Release','--list-tests',$taskFlag) + $flags)
    $names = @($discovery -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_.StartsWith('AvaloniaHome.Tests.', [StringComparison]::Ordinal) })
    Write-Json (Join-Path $diagnostics 'discovery.json') ([ordered]@{ names = $names; total = $names.Count; filter = $null })
    if ($names.Count -ne 6) { throw 'Original owning project did not discover all six cases.' }
    $tests = Join-Path $diagnostics 'test-results'; New-Item -ItemType Directory -Path $tests | Out-Null
    $deferredTestError = $null
    try { [void](Invoke-DotNet 'owning-test-release' (@('test',$project,'--no-build','--no-restore','-c','Release','--logger','trx;LogFileName=home-original-six.trx','--results-directory',$tests,$taskFlag) + $flags)) }
    catch { $deferredTestError = $_ }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $tests 'home-original-six.trx') -Raw
    $ns = [Xml.XmlNamespaceManager]::new($trx.NameTable); $ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $counter = $trx.SelectSingleNode('//t:Counters',$ns); $cases = @($trx.SelectNodes('//t:UnitTestResult',$ns))
    $testRuntimeAfter = @(Get-FileCatalog $testRuntimeRoot -Exclusive)
    Write-Json (Join-Path $diagnostics 'owning-runtime-after.json') $testRuntimeAfter
    if (-not (Same-Catalog $testRuntimeBefore $testRuntimeAfter)) { throw 'Actual original test runtime changed during discovery/execution.' }
    $result.managedCases = [ordered]@{ total = [int]$counter.total; executed = [int]$counter.executed; passed = [int]$counter.passed; failed = [int]$counter.failed; notExecuted = [int]$counter.notExecuted; names = @($cases | ForEach-Object { $_.testName }); filter = $null; assertions = $null; sourceAssertionCallSitesNewClass = 54 }
    Save-Result
    if ($deferredTestError) { throw $deferredTestError }
    if ($counter.total -ne 6 -or $counter.executed -ne 6 -or $counter.passed -ne 6 -or $counter.failed -ne 0 -or $counter.notExecuted -ne 0 -or $cases.Count -ne 6) { throw 'All original owning six cases must pass without skips.' }
    foreach ($method in $catalog.ordinaryTests.originalMethods) { if (@($cases | Where-Object { $_.testName.Contains($method, [StringComparison]::Ordinal) }).Count -ne [int]$catalog.ordinaryTests.casesByMethod.$method) { throw "Exact original owning method is missing or duplicated: $method" } }
    if (@($cases | Where-Object { $_.testName -like '*HomeHostOriginalLifetimeTests.*' }).Count -ne 5 -or @($cases | Where-Object { $_.testName -like '*HomeHostRenderTests.Native_home_host_loads_canonical_cui_into_a_measured_window*' }).Count -ne 1) { throw 'Original render case or new original lifetime cases are missing.' }
    $nativeProject = 'apps/Home/src/AvaloniaHome/AvaloniaHome.csproj'
    $result.stage = 'native-release-publish'
    [void](Invoke-DotNet 'native-home-release' (@('build',$nativeProject,'--no-restore','-c','Release','-f','net10.0-windows',$taskFlag) + $flags))
    $publish = Join-Path $output 'publish'
    [void](Invoke-DotNet 'publish-release-win-x64' (@('publish',$nativeProject,'-c','Release','-f','net10.0-windows','-r','win-x64','--self-contained','true','-p:PublishSingleFile=false','-p:PublishReadyToRun=false','-p:DebugType=portable',$taskFlag,'-o',$publish,'--nologo') + $flags))
    foreach ($required in @('AvaloniaHome.exe','AvaloniaHome.deps.json','AvaloniaHome.runtimeconfig.json','UI/Home.cui')) { if (-not (Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) { throw "Original package sidecar missing: $required" } }
    $runtimeBefore = @(Get-FileCatalog $publish); Write-Json (Join-Path $diagnostics 'runtime-before.json') $runtimeBefore
    $publication = Join-Path $output 'public-artifact'; New-Item -ItemType Directory -Path $publication | Out-Null
    $zip = Join-Path $publication 'home-win-x64.zip'; [IO.Compression.ZipFile]::CreateFromDirectory($publish,$zip)
    $zipItem = Get-Item -LiteralPath $zip; $zipSHA = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $result.package = [ordered]@{ sourceCommit = $ExpectedCommit; files = $runtimeBefore.Count; zipBytes = $zipItem.Length; zipSHA256 = $zipSHA; authenticodeStatus = (Get-AuthenticodeSignature -LiteralPath (Join-Path $publish 'AvaloniaHome.exe')).Status.ToString(); installedIdentity = 'NOT_SUPPLIED'; donorOrCleanPcAcceptance = $false }
    Write-Json (Join-Path $publication 'publish-manifest.json') ([ordered]@{ sourceCommit = $ExpectedCommit; publishRoot = $publish; files = $runtimeBefore; archiveBytes = $zipItem.Length; archiveSHA256 = $zipSHA; scope = $result.qualification })
    Write-Json (Join-Path $publication 'seal.json') ([ordered]@{ sourceCommit = $ExpectedCommit; manifestSHA256 = (Get-FileHash -LiteralPath (Join-Path $publication 'publish-manifest.json') -Algorithm SHA256).Hash.ToLowerInvariant(); archiveSHA256 = $zipSHA; archiveBytes = $zipItem.Length })
    if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value 'package_ready=true' }
    $parts = Join-Path $output 'public-parts'; New-Item -ItemType Directory -Path $parts | Out-Null
    $partLimit = 19MB; $partCount = [int][Math]::Ceiling($zipItem.Length / $partLimit)
    if ($partCount -gt 8) { throw 'Original package exceeds the declared eight-part transport bound; retain whole artifact and report failure.' }
    $partRows = @(); $sourceStream = [IO.File]::OpenRead($zip)
    try {
        for ($number=1; $number -le $partCount; $number++) {
            $partDirectory = Join-Path $parts ('part{0:d2}' -f $number); New-Item -ItemType Directory -Path $partDirectory | Out-Null
            $partPath = Join-Path $partDirectory 'home-win-x64.zip.part'; $partStream = [IO.File]::Create($partPath)
            try {
                [long]$remaining = [Math]::Min($partLimit,$sourceStream.Length-$sourceStream.Position); $buffer = [byte[]]::new(65536)
                while ($remaining -gt 0) { $read=$sourceStream.Read($buffer,0,[int][Math]::Min($buffer.Length,$remaining)); if ($read -le 0) { throw 'Unexpected original ZIP EOF.' }; $partStream.Write($buffer,0,$read); $remaining-=$read }
            } finally { $partStream.Dispose() }
            $partRows += [ordered]@{ part=$number; path=('part{0:d2}/home-win-x64.zip.part' -f $number); bytes=(Get-Item -LiteralPath $partPath).Length; sha256=(Get-FileHash -LiteralPath $partPath -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
    } finally { $sourceStream.Dispose() }
    $reassembly = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    [long]$reassembledBytes = 0
    try {
        foreach ($part in $partRows) {
            $partPath = Join-Path $parts $part.path; $stream = [IO.File]::OpenRead($partPath)
            try { $buffer=[byte[]]::new(65536); while (($read=$stream.Read($buffer,0,$buffer.Length)) -gt 0) { $reassembly.AppendData($buffer,0,$read); $reassembledBytes += $read } } finally { $stream.Dispose() }
        }
        $reassembledSHA = [Convert]::ToHexString($reassembly.GetHashAndReset()).ToLowerInvariant()
    } finally { $reassembly.Dispose() }
    if ($reassembledBytes -ne $zipItem.Length -or $reassembledSHA -cne $zipSHA) { throw 'Ordered original ZIP parts do not reproduce the sealed complete archive.' }
    $partsManifest = [ordered]@{ sourceCommit=$ExpectedCommit; originalArchiveBytes=$zipItem.Length; originalArchiveSHA256=$zipSHA; partBytesLimit=$partLimit; orderedReassemblyVerified=$true; parts=$partRows; reassembly='Concatenate in numeric order; require original whole size/SHA before ZIP use.' }
    foreach ($partDirectory in @(Get-ChildItem -LiteralPath $parts -Directory)) {
        Write-Json (Join-Path $partDirectory.FullName 'parts-manifest.json') $partsManifest
        Copy-Item -LiteralPath (Join-Path $publication 'publish-manifest.json'),(Join-Path $publication 'seal.json') -Destination $partDirectory.FullName
    }
    if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "part_count=$partCount"; Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value 'publication_ready=true' }
    $extracted = Join-Path $output 'extracted'; [IO.Compression.ZipFile]::ExtractToDirectory($zip,$extracted)
    if (-not (Same-Catalog $runtimeBefore @(Get-FileCatalog $extracted))) { throw 'Actual extracted original package differs from the complete published file manifest.' }
    $actualLocalData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($actualLocalData)) { throw 'The original OS local application data route is unavailable.' }
    $probeProfileRoot = Join-Path $actualLocalData '9to1/Home'
    $result.profileRoute = 'Original FileHomeCoreStateStore.CreateDefault: OS LocalApplicationData/9to1/Home; unchanged disposable runner account, no path override/seed/actor.'
    $result.profileInitiallyExists = Test-Path -LiteralPath $probeProfileRoot
    $profileInitial = @(if ($result.profileInitiallyExists) { Get-FileCatalog $probeProfileRoot -Exclusive }); Write-Json (Join-Path $diagnostics 'profile-initial-hashes.json') $profileInitial
    $result.stage = 'original-native-explicit-quit'
    [void](Invoke-OriginalNativeQuit 'native-first' (Join-Path $extracted 'AvaloniaHome.exe'))
    if (-not (Test-Path -LiteralPath $probeProfileRoot -PathType Container)) { throw 'Actual original default store root was not established by the native host.' }
    $profileFirst = @(Get-FileCatalog $probeProfileRoot -Exclusive); Write-Json (Join-Path $diagnostics 'profile-after-first-hashes.json') $profileFirst
    [void](Invoke-OriginalNativeQuit 'native-second-same-profile' (Join-Path $extracted 'AvaloniaHome.exe'))
    $profileSecond = @(Get-FileCatalog $probeProfileRoot -Exclusive); Write-Json (Join-Path $diagnostics 'profile-after-second-hashes.json') $profileSecond
    if ($result.nativeProcesses.Count -ne 2 -or $result.nativeProcesses[0].pid -eq $result.nativeProcesses[1].pid) { throw 'Two distinct actual fresh native processes are required.' }
    $result.persistedDefaultStateExists = Test-Path -LiteralPath (Join-Path $probeProfileRoot 'home-core-state.json') -PathType Leaf
    $result.sameProfileReadOnlyReopenEquivalent = Same-Catalog $profileFirst $profileSecond
    if (-not $result.sameProfileReadOnlyReopenEquivalent) { throw 'Fresh-process same-profile physical contents changed; preserve first actual mismatch.' }
    $runtimeAfter = @(Get-FileCatalog $extracted -Exclusive); Write-Json (Join-Path $diagnostics 'runtime-after.json') $runtimeAfter
    if (-not (Same-Catalog $runtimeBefore $runtimeAfter)) { throw 'Original extracted runtime files changed across native controls.' }
    $result.status = 'PASS_ORIGINAL_HOME_MANAGED_SIX_AND_VISIBLE_EXPLICIT_NATIVE_QUIT_SCOPED'
} catch {
    $result.status = 'FAIL_OR_INCOMPLETE'; $result.failureType = $_.Exception.GetType().Name
    Write-Error -Message ("Original Home validation failed at {0}: {1}; controlled phase and original logs retained." -f $result.stage, $result.failureType) -ErrorAction Continue
} finally {
    try {
        $after = @(Get-TreeSnapshot); Write-Json (Join-Path $diagnostics 'source-after.json') $after
        if ($null -ne $before) { $result.sourceAfterUnchanged = Same-Catalog $before $after; if (-not $result.sourceAfterUnchanged) { throw 'Actual source bodies changed during original execution.' } }
        if (@(& git status --porcelain).Count -ne 0) { throw 'Source checkout was modified during original execution.' }
    } catch { $result.status = 'FAIL_OR_INCOMPLETE'; $result.finalCustodyFailureType = $_.Exception.GetType().Name }
    $result.acceptedNativeShutdownScope = $result.status -ceq 'PASS_ORIGINAL_HOME_MANAGED_SIX_AND_VISIBLE_EXPLICIT_NATIVE_QUIT_SCOPED' -and $result.sourceAfterUnchanged
    Save-Result
}
if (-not $result.acceptedNativeShutdownScope) { exit 1 }
exit 0
