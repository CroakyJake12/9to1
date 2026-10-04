[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

# This builds existing entry points. It does not supply Home authority, sign a
# package, substitute a bootstrap, run full app acceptance or publish a release.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$catalog = Get-Content -Raw -LiteralPath (Join-Path $repo '.github/validation/windows-package-probe.json') | ConvertFrom-Json
$selected = @($catalog.targets | Where-Object { $_.key -ceq $Target })
if ($selected.Count -ne 1) { throw 'Select one exact declared entry point.' }
$entry = $selected[0]
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Evidence/output directory must be new.' }
if ($output.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Keep generated evidence outside the source checkout.'
}
[void][IO.Directory]::CreateDirectory($output)
$result = [ordered]@{
    schemaVersion = 1; target = $Target; sourceBasis = $catalog.sourceBasis
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    status = 'NOT_RUN'; stage = 'preflight'; package = $null; files = @()
    commands = @(); extractionVerified = $false; launch = $null
    acceptanceVerified = $false; fullAppAcceptance = 'NOT_RUN'
    homeBootstrapAndCompatibility = 'NOT_RUN'; pcDelivery = 'NOT_RUN'
    qualification = $entry.qualification
    cleanProfileQualification = 'Disposable hosted runner account; SDK exists on runner. This is not a no-SDK clean-install or full GUI acceptance test.'
    failure = $null
}
$probe = $null
$exitCode = 1

function Write-Result {
    $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
}

function Invoke-DotNet([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $output ($Name + '.log')
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $code = $LASTEXITCODE
    $result.commands += [ordered]@{ name = $Name; argv = $Arguments; exitCode = $code; log = [IO.Path]::GetFileName($log) }
    Write-Result
    if ($code -ne 0) { throw "$Name failed with exit code $code." }
}

try {
    if (-not [OperatingSystem]::IsWindows()) { throw 'Actual Windows execution is required.' }
    if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
        throw 'This probe declares win-x64 only; do not infer ARM/laptop compatibility.'
    }
    $result.windowsVersion = [Environment]::OSVersion.VersionString
    $result.architecture = 'win-x64'
    $result.runnerImage = $env:ImageVersion
    $result.workflowCommit = (& git -C $repo rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot bind workflow commit.' }
    & git -C $repo merge-base --is-ancestor $catalog.sourceBasis HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Candidate is outside the reviewed source lineage.' }
    $changed = @(& git -C $repo diff --name-only $catalog.sourceBasis HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot compare immutable source basis.' }
    foreach ($path in $changed) {
        if ($catalog.proposalPaths -cnotcontains $path) { throw "Undeclared source change: $path" }
    }
    foreach ($pin in $catalog.sourcePins) {
        $actual = (Get-FileHash -LiteralPath (Join-Path $repo $pin.path) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -cne $pin.sha256) { throw "Source pin mismatch: $($pin.path)" }
    }
    Write-Result
    $result.stage = 'toolchain'
    Invoke-DotNet 'sdk-info' @('--info')
    $tasksProject = Join-Path $repo 'framework/CUI/Compiler/CakeOS.Cui.Build.Tasks/CakeOS.Cui.Build.Tasks.csproj'
    Invoke-DotNet 'cui-tasks-release' @('build', $tasksProject, '-c', 'Release', '--nologo')
    $tasks = Join-Path $repo 'framework/CUI/Compiler/CakeOS.Cui.Build.Tasks/bin/Release/netstandard2.0/CakeOS.Cui.Build.Tasks.dll'
    if (-not (Test-Path -LiteralPath $tasks -PathType Leaf)) { throw 'Original CUI build-task assembly is missing.' }
    $result.cuiBuildTasksSha256 = (Get-FileHash -LiteralPath $tasks -Algorithm SHA256).Hash.ToLowerInvariant()
    $result.stage = 'publish'
    $publish = Join-Path $output 'publish'
    Invoke-DotNet 'publish-release-win-x64' @(
        'publish', (Join-Path $repo $entry.project), '-c', 'Release', '-f', $entry.framework,
        '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=false',
        '-p:PublishReadyToRun=false', '-p:DebugType=portable', "-p:CuiBuildTasksLocation=$tasks",
        '-o', $publish, '--nologo'
    )
    $exe = Join-Path $publish $entry.executable
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Declared executable was not published.' }
    foreach ($required in @($entry.requiredFiles)) {
        if (-not (Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) {
            throw "Declared sidecar is missing: $required"
        }
    }
    $items = @(Get-ChildItem -LiteralPath $publish -Recurse -Force)
    foreach ($item in $items) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Package contains a reparse point.' }
    }
    $files = @($items | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)
    foreach ($file in $files) {
        $result.files += [ordered]@{
            path = [IO.Path]::GetRelativePath($publish, $file.FullName).Replace('\', '/')
            bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    $result.authenticodeStatus = $signature.Status.ToString()
    $zip = Join-Path $output ($Target + '-win-x64-unaccepted.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip)
    $result.package = [ordered]@{ file = [IO.Path]::GetFileName($zip); bytes = (Get-Item -LiteralPath $zip).Length; sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() }
    $result.stage = 'extract'
    $install = Join-Path $output 'fresh-extraction'
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $install)
    $extracted = @(Get-ChildItem -LiteralPath $install -Recurse -File -Force)
    if ($extracted.Count -ne $result.files.Count) { throw 'Extracted file set differs from published package.' }
    foreach ($file in $result.files) {
        $path = Join-Path $install $file.path
        if ((Get-Item -LiteralPath $path).Length -ne $file.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) {
            throw "Extracted file mismatch: $($file.path)"
        }
    }
    $result.extractionVerified = $true
    $result.stage = 'launch'
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = Join-Path $install $entry.executable
    $start.WorkingDirectory = $install
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($variable in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_HOST_PATH', 'MSBuildSDKsPath')) {
        [void]$start.Environment.Remove($variable)
    }
    $probe = [Diagnostics.Process]::Start($start)
    $stdout = $probe.StandardOutput.ReadToEndAsync()
    $stderr = $probe.StandardError.ReadToEndAsync()
    $launch = [ordered]@{ processId = $probe.Id; sessionId = $probe.SessionId; windowObserved = $false; windowTitle = $null; closeRequested = $false; exited = $false; forcedCleanup = $false; exitCode = $null }
    $result.launch = $launch
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt 30 -and -not $probe.HasExited) {
        $probe.Refresh()
        if ($probe.MainWindowHandle -ne [IntPtr]::Zero) {
            $launch.windowObserved = $true
            $launch.windowTitle = $probe.MainWindowTitle
            break
        }
        Start-Sleep -Milliseconds 200
    }
    if (-not $probe.HasExited -and $launch.windowObserved) { $launch.closeRequested = $probe.CloseMainWindow() }
    if (-not $probe.WaitForExit(10000)) {
        $launch.forcedCleanup = $true
        $probe.Kill($true)
        if (-not $probe.WaitForExit(10000)) { throw 'Task-owned process did not exit after bounded cleanup.' }
    }
    $launch.exited = $probe.HasExited
    $launch.exitCode = $probe.ExitCode
    [IO.File]::WriteAllText((Join-Path $output 'launch.stdout.log'), $stdout.WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult())
    [IO.File]::WriteAllText((Join-Path $output 'launch.stderr.log'), $stderr.WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult())
    $result.stage = 'source-readback'
    & git -C $repo diff --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Tracked source changed during the normal build/launch probe.' }
    if (-not $launch.windowObserved) { throw 'No native main window was observed; no launch acceptance inferred.' }
    if ($launch.forcedCleanup) { throw 'Window was observed, but graceful test shutdown is unverified; forced cleanup is retained.' }
    if ($launch.exitCode -ne 0) { throw 'Executable exited with failure.' }
    $result.status = 'PACKAGE_EXTRACTION_AND_WINDOW_PROBE_PASS_UNACCEPTED'
    $exitCode = 0
}
catch {
    $result.status = 'FAILED_OR_BLOCKED_UNACCEPTED'
    $result.failure = [ordered]@{ type = $_.Exception.GetType().FullName; message = $_.Exception.Message }
    Write-Error -ErrorAction Continue $_
}
finally {
    if ($null -ne $probe) {
        if (-not $probe.HasExited) {
            $result.launch.forcedCleanup = $true
            try {
                $probe.Kill($true)
                if (-not $probe.WaitForExit(10000)) { $result.cleanupFailure = 'Task-owned process did not exit after bounded cleanup.' }
            }
            catch { $result.cleanupFailure = $_.Exception.GetType().FullName }
        }
        $probe.Dispose()
    }
    Write-Result
}
exit $exitCode
