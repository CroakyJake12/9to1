[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageInput,
    [Parameter(Mandatory = $true)][string]$ObservationInput,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$ExpectedCommit,
    [Parameter(Mandatory = $true)][string]$ExpectedRunId,
    [Parameter(Mandatory = $true)][string]$ExpectedRunAttempt
)

# Windows PowerShell 5.1 / standard Windows UIAutomation and keyboard input.
# Original Present package; ordinary default profile; UIAutomation and owned keyboard only.
# No native app test hook, model/API write, SDK build, injected config or Home bootstrap.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$catalog = Get-Content -Raw -LiteralPath (Join-Path $repo '.github/validation/present-workspace-windows.json') | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Evidence directory must be new.' }
if ($output.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep runtime evidence outside the source checkout.' }
[void][IO.Directory]::CreateDirectory($output)
$result = [ordered]@{
    schemaVersion = 1; status = 'NOT_RUN'; stage = 'preflight'; checks = @(); launches = @(); screenshots = @(); streamDrains = @()
    producerRunId = $ExpectedRunId; producerCommit = $ExpectedCommit; producerRunAttempt = $ExpectedRunAttempt
    originalArtifacts = @(); package = $null
    packageHashVerified = $false; manifestHashVerified = $false; extractedFilesVerified = $false
    forcedCleanup = $false; acceptanceVerified = $false; fullAppAcceptance = 'NOT_RUN'
    installedHomeTuple = 'NOT_RUN'; cleanPcDelivery = 'NOT_RUN'; failure = $null; donorImportExport = 'NOT_RUN'; completeAccessibilityAudit = 'NOT_RUN'; streamCharacterLimit = 1048576
    qualification = 'Actual immutable packaged executable on a disposable SDK-present hosted Windows runner. Bounded native creation, UIA/keyboard edits, local save, picker cancellation, library open and normal process reopen only; no Home installed tuple, signing, donor matrix, Android, clean-PC or full-app acceptance.'
}
$process = $null
$exitCode = 1
$script:evidenceWriteFailure = $null

function Write-Result {
    try { $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding UTF8 }
    catch {
        if ($null -eq $script:evidenceWriteFailure) { $script:evidenceWriteFailure = [ordered]@{ type = $_.Exception.GetType().FullName; stage = $result.stage } }
        $result.evidenceWriteFailure = $script:evidenceWriteFailure
        throw
    }
}
function Apply-Final-Failure-Guard {
    $cleanupFailed = $result.forcedCleanup -or
        ($result.Contains('cleanupDrainFailure') -and -not [string]::IsNullOrWhiteSpace([string]$result.cleanupDrainFailure)) -or
        ($result.Contains('cleanupFailure') -and -not [string]::IsNullOrWhiteSpace([string]$result.cleanupFailure)) -or
        ($result.Contains('evidenceWriteFailure') -and $null -ne $result.evidenceWriteFailure)
    if ($cleanupFailed) {
        $result.status = 'FAILED_OR_BLOCKED_UNACCEPTED'
        $result.finalizationFailure = $true
        $script:exitCode = 1
    }
}
function Check([bool]$Condition, [string]$Name) {
    $result.checks += [ordered]@{ name = $Name; passed = $Condition; observedAtUtc = [DateTimeOffset]::UtcNow.ToString('o') }
    Write-Result
    if (-not $Condition) { throw $Name }
}
function Wait-Observed([scriptblock]$Probe, [string]$Name, [int]$Seconds = 20) {
    $until = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    do {
        $observed = & $Probe
        if ($observed) { return $observed }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $until)
    $result.observationTimeout = [ordered]@{ name = $Name; seconds = $Seconds }
    Write-Result
    throw 'A controlled observation deadline expired.'
}
function Find-InputFile([string]$Root, [string]$Name) {
    $files = @(Get-ChildItem -LiteralPath $Root -Recurse -Force)
    foreach ($file in $files) {
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Input contains a reparse point.' }
    }
    $matches = @($files | Where-Object { -not $_.PSIsContainer -and $_.Name -ceq $Name })
    if ($matches.Count -ne 1) { throw "Expected one exact original input: $Name" }
    return $matches[0].FullName
}
function Find-Control([System.Windows.Automation.AutomationElement]$Window, [string]$Id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    return $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Wait-Edit([System.Windows.Automation.AutomationElement]$Window, [string]$Id, [string]$Name) {
    return Wait-Observed {
        $candidate = Find-Control $Window $Id
        if ($null -ne $candidate -and $candidate.Current.IsEnabled -and -not $candidate.Current.IsOffscreen -and $candidate.Current.BoundingRectangle.Width -gt 0 -and $candidate.Current.BoundingRectangle.Height -gt 0) { return $candidate }
        return $null
    } $Name
}
function Observe-Edit([System.Windows.Automation.AutomationElement]$Element, [string]$ExpectedName) {
    $current = $Element.Current
    Check ($current.ProcessId -eq $process.Id) 'Accessible editor belongs to the exact launched process'
    Check ($current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and $current.Name -ceq $ExpectedName) "Accessible Edit name: $ExpectedName"
    Check ($current.IsEnabled -and $current.IsKeyboardFocusable -and -not $current.IsOffscreen -and $current.BoundingRectangle.Width -gt 0 -and $current.BoundingRectangle.Height -gt 0) "Visible, enabled, keyboard-focusable editor: $ExpectedName"
    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    Check (-not $pattern.Current.IsReadOnly) "Editable native ValuePattern: $ExpectedName"
    return $pattern
}
function Assert-OwnedFocus([System.Windows.Automation.AutomationElement]$Element) {
    if ($process.HasExited -or -not [PresentPackageInput]::OwnsForeground($process.Id)) { throw 'Keyboard input refused outside the exact app foreground process.' }
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($null -eq $focused -or $focused.Current.ProcessId -ne $process.Id -or
        $focused.Current.AutomationId -cne $Element.Current.AutomationId -or -not $Element.Current.HasKeyboardFocus) {
        throw 'Keyboard input refused without the exact native control focus.'
    }
}
function Focus-Edit([System.Windows.Automation.AutomationElement]$Element) {
    Check ([PresentPackageInput]::SetForegroundWindow($process.MainWindowHandle)) 'Native app admitted foreground activation'
    $Element.SetFocus()
    [void](Wait-Observed { $Element.Current.HasKeyboardFocus -and [PresentPackageInput]::OwnsForeground($process.Id) } 'Exact native editor focus')
    Assert-OwnedFocus $Element
}
function Type-Text([System.Windows.Automation.AutomationElement]$Element, [string]$Text) {
    Focus-Edit $Element
    Assert-OwnedFocus $Element
    [PresentPackageInput]::Chord(0x11, 0x41) # Ctrl+A
    Assert-OwnedFocus $Element
    [PresentPackageInput]::TypeText($Text)
    $value = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    [void](Wait-Observed { $value.Current.Value -ceq $Text } 'Real native keyboard text value')
    Check ($value.Current.Value -ceq $Text) 'Actual keyboard input updated the native editor value'
}
function Read-Document([string]$Path) { return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json }
function Document-Files {
    if (-not (Test-Path -LiteralPath $documentsRoot -PathType Container)) { return @() }
    $all = @(Get-ChildItem -LiteralPath $documentsRoot -Recurse -Force)
    foreach ($item in $all) { if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Native repository contains a reparse point.' } }
    return @($all | Where-Object { -not $_.PSIsContainer -and $_.Name -ceq 'current.json' })
}
function Observe-Button([System.Windows.Automation.AutomationElement]$Element, [string]$ExpectedName) {
    $current = $Element.Current
    Check ($current.ProcessId -eq $process.Id -and $current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $current.Name -ceq $ExpectedName) "Exact owned accessible Button: $ExpectedName"
    Check ($current.IsEnabled -and -not $current.IsOffscreen -and $current.BoundingRectangle.Width -gt 0 -and $current.BoundingRectangle.Height -gt 0) "Visible enabled native Button: $ExpectedName"
    return $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
}
function Invoke-Button([System.Windows.Automation.AutomationElement]$Element, [string]$ExpectedName) {
    $pattern = Observe-Button $Element $ExpectedName
    $pattern.Invoke()
}
function File-Action([System.Windows.Automation.AutomationElement]$Window, [string]$Label) {
    $menu = Wait-Observed { Find-Control $Window 'Present.Menu.File' } 'Native File menu control'
    Invoke-Button $menu 'File'
    $card = Wait-Observed { Find-Control $Window 'PopupMenuCard' } 'Actual visible native File popup'
    Check ($card.Current.ProcessId -eq $process.Id -and $card.Current.Name -ceq 'File menu') 'Actual popup is the owning File menu'
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Label)
    $item = Wait-Observed { $card.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition) } "Actual File action: $Label"
    Invoke-Button $item $Label
}
function Rail-Buttons([System.Windows.Automation.AutomationElement]$Window) {
    $all = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    return @($all | Where-Object { $_.Current.AutomationId.StartsWith('Present.Rail.', [StringComparison]::Ordinal) -and $_.Current.ProcessId -eq $process.Id })
}
function Check-Document([string]$Path, [string]$Id, [string]$Title, [string]$Notes, [string]$SlideId) {
    $doc = Read-Document $Path
    $slide = @($doc.slides | Where-Object { $_.id -ceq $SlideId })
    return $doc.id -ceq $Id -and $doc.title -ceq $Title -and $doc.schemaVersion -eq 3 -and @($doc.slides).Count -eq 2 -and $slide.Count -eq 1 -and $slide[0].speakerNotes -ceq $Notes
}
function Start-Present([string]$Label) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe; $start.Arguments = ''; $start.WorkingDirectory = $install
    $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $script:process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'The actual packaged executable did not start.' }
    $script:stdout = [PresentPackageInput]::ReadBoundedAsync($process.StandardOutput, 1048576)
    $script:stderr = [PresentPackageInput]::ReadBoundedAsync($process.StandardError, 1048576)
    $script:launchLabel = $Label
    $script:launchRecord = [ordered]@{ label = $Label; processId = $process.Id; windowObserved = $false; closeRequested = $false; exited = $false; exitCode = $null }
    $result.launches += $launchRecord; Write-Result
    [void](Wait-Observed {
        $process.Refresh()
        if ($process.HasExited) { throw 'Packaged app exited before native window readiness.' }
        $process.MainWindowHandle -ne [IntPtr]::Zero
    } 'Actual packaged native window')
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Check ($window.Current.ProcessId -eq $process.Id -and $window.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) 'Actual HWND exposes a native UIAutomation Window'
    $launchRecord.windowObserved = $true; $launchRecord.windowTitle = $window.Current.Name; Write-Result
    return $window
}
function Drain-OwnedLogs {
    $allDrained = $true
    foreach ($stream in @(@{ name = 'stdout'; task = $stdout }, @{ name = 'stderr'; task = $stderr })) {
        $drain = [ordered]@{ launch = $launchLabel; stream = $stream.name; timeoutMilliseconds = 5000; completed = $false; failureType = $null }
        try {
            if ($stream.task.Wait(5000)) {
                # Result is read only after the bounded wait observes completion.
                $text = $stream.task.Result
                $text | Set-Content -LiteralPath (Join-Path $output ($launchLabel + '.' + $stream.name + '.log')) -Encoding UTF8
                if ($stream.name -ceq 'stderr') { $script:stderrText = $text }
                $drain.completed = $true
            } else { $allDrained = $false; $drain.failureType = 'BoundedStreamDrainTimeout' }
        } catch { $allDrained = $false; $drain.failureType = $_.Exception.GetType().FullName }
        $result.streamDrains += $drain
    }
    Write-Result
    return $allDrained
}
function Close-Present {
    $launchRecord.closeRequested = $process.CloseMainWindow()
    Check $launchRecord.closeRequested 'Actual native window accepted graceful close'
    Check ($process.WaitForExit(20000)) 'Actual packaged process exited after graceful close'
    $launchRecord.exited = $true; $launchRecord.exitCode = $process.ExitCode
    Check (Drain-OwnedLogs) 'Actual process stdout/stderr drain completed within each five-second bound'
    Check ($process.ExitCode -eq 0) 'Actual packaged native close exit code is zero'
    Check ([string]::IsNullOrWhiteSpace($stderrText)) 'Actual packaged app stderr is empty'
    $process.Dispose(); $script:process = $null; Write-Result
}
function Capture-Window([string]$Name) {
    Check ([PresentPackageInput]::OwnsForeground($process.Id)) 'Screenshot is bound to the actual app foreground process'
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $r = $window.Current.BoundingRectangle
    $bounds = New-Object Drawing.Rectangle([int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    $visible = [Drawing.Rectangle]::Intersect($bounds, [Windows.Forms.SystemInformation]::VirtualScreen)
    Check ($visible.Width -gt 0 -and $visible.Height -gt 0 -and $visible.Width -le 1920 -and $visible.Height -le 1080) 'Actual app screenshot has bounded visible screen geometry'
    $bitmap = New-Object Drawing.Bitmap($visible.Width, $visible.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($visible.Location, [Drawing.Point]::Empty, $visible.Size)
        $bitmap.Save((Join-Path $output $Name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    $image = Join-Path $output $Name
    Check ((Get-Item -LiteralPath $image).Length -le 9437184) 'Selected native screenshot fits its nine-MiB public output limit'
    $result.screenshots += [ordered]@{ file = $Name; bytes = (Get-Item -LiteralPath $image).Length; sha256 = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash.ToLowerInvariant(); visibleWidth = $visible.Width; visibleHeight = $visible.Height }
    Write-Result
}

try {
    Check ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT -and [Environment]::Is64BitProcess) 'Actual Windows x64 PowerShell host'
    Check ([Environment]::UserInteractive) 'Visible interactive Windows desktop available for real keyboard and screen observations'
    $result.windowsVersion = [Environment]::OSVersion.VersionString; $result.runnerImage = $env:ImageVersion
    $result.powerShellVersion = $PSVersionTable.PSVersion.ToString()
    $result.sdkInventory = @(& dotnet --list-sdks); Check ($LASTEXITCODE -eq 0 -and $result.sdkInventory.Count -gt 0) 'Existing hosted runner SDK-present qualification observed'
    $result.workflowCommit = (& git -C $repo rev-parse HEAD).Trim(); Check ($LASTEXITCODE -eq 0) 'Current harness Git commit observed'
    & git -C $repo merge-base --is-ancestor $catalog.sourceBasis HEAD
    Check ($LASTEXITCODE -eq 0) 'Harness descends from the exact immutable producer source'
    $changes = @(& git -C $repo diff --name-only $catalog.sourceBasis HEAD)
    Check ($LASTEXITCODE -eq 0 -and @($changes | Where-Object { $catalog.harnessPaths -cnotcontains $_ }).Count -eq 0) 'Only declared workflow/control files changed from the producer'
    foreach ($pin in $catalog.sourcePins) { Check ((Get-FileHash -LiteralPath (Join-Path $repo $pin.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $pin.sha256) "Exact native source pin: $($pin.path)" }
    Check ($result.workflowCommit -ceq $ExpectedCommit) 'UI controls run on the exact same producer commit'
    Check ($env:GITHUB_RUN_ID -ceq $ExpectedRunId -and $env:GITHUB_RUN_ATTEMPT -ceq $ExpectedRunAttempt) 'UI controls bind the same actual workflow run and attempt'
    $result.stage = 'same-run-new-package-custody'
    $manifestPath = Find-InputFile $ObservationInput 'present-producer-manifest.json'
    $sealPath = Find-InputFile $ObservationInput 'seal.json'
    $seal = Get-Content -Raw -LiteralPath $sealPath | ConvertFrom-Json
    Check ($seal.sourceCommit -ceq $ExpectedCommit -and [string]$seal.runId -ceq $ExpectedRunId -and [string]$seal.runAttempt -ceq $ExpectedRunAttempt) 'New package seal binds actual current commit, run and attempt'
    Check ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $seal.manifestSHA256) 'Exact sealed new producer manifest SHA256'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    Check ($manifest.workflowCommit -ceq $ExpectedCommit -and [string]$manifest.runId -ceq $ExpectedRunId -and [string]$manifest.runAttempt -ceq $ExpectedRunAttempt -and $manifest.target -ceq 'present') 'New producer manifest binds Present and same exact producer tuple'
    $zip = Find-InputFile $PackageInput 'present-win-x64-unaccepted.zip'
    Check ((Get-Item -LiteralPath $zip).Length -eq $seal.archiveBytes -and $seal.archiveBytes -eq $manifest.package.bytes -and (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $seal.archiveSHA256 -and $seal.archiveSHA256 -ceq $manifest.package.sha256) 'Exact newly built inner package bytes and sealed SHA256'
    $result.package = $manifest.package
    $result.packageHashVerified = $true; $result.manifestHashVerified = $true
    $result.producerManifestSHA256 = $seal.manifestSHA256
    Add-Type -AssemblyName System.IO.Compression.FileSystem, UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
    $install = Join-Path $output 'install'
    $expectedFiles = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    $manifestBytes = [long]0
    foreach ($file in $manifest.files) {
        $normalized = $file.path.Replace('\', '/')
        if ($expectedFiles.ContainsKey($normalized) -or [long]$file.bytes -lt 0) { throw 'Duplicate or invalid original manifest entry.' }
        $expectedFiles.Add($normalized, $file); $manifestBytes += [long]$file.bytes
    }
    Check ($expectedFiles.Count -eq $manifest.package.fileCount -and $manifestBytes -eq $manifest.package.uncompressedBytes) 'Exact sealed new manifest file count and aggregate extracted bytes'
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $archive.Entries) {
            $full = [IO.Path]::GetFullPath((Join-Path $install $entry.FullName))
            if (-not $full.StartsWith($install + '\', [StringComparison]::OrdinalIgnoreCase) -or
                ($entry.ExternalAttributes -band 1024) -ne 0 -or (($entry.ExternalAttributes -shr 16) -band 61440) -eq 40960) { throw 'Unsafe package archive entry.' }
            if ($entry.FullName.EndsWith('/')) { continue }
            $normalized = $entry.FullName.Replace('\', '/')
            if (-not $seen.Add($normalized) -or -not $expectedFiles.ContainsKey($normalized) -or $entry.Length -ne $expectedFiles[$normalized].bytes) { throw 'Archive file differs from exact original manifest.' }
        }
        Check ($seen.Count -eq $expectedFiles.Count) 'Original archive contains every exact manifest member once'
    } finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $install)
    foreach ($entry in @(Get-ChildItem -LiteralPath $install -Recurse -Force)) { if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Extracted package contains a reparse point.' } }
    $files = @(Get-ChildItem -LiteralPath $install -Recurse -File -Force)
    Check ($files.Count -eq @($manifest.files).Count) 'Extracted package contains the exact original file count'
    foreach ($file in $manifest.files) {
        $path = [IO.Path]::GetFullPath((Join-Path $install $file.path))
        if (-not $path.StartsWith($install + '\', [StringComparison]::OrdinalIgnoreCase) -or
            (Get-Item -LiteralPath $path).Length -ne $file.bytes -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) { throw 'Extracted original package file mismatch.' }
    }
    $result.extractedFilesVerified = $true; Write-Result
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
public static class PresentPackageInput {
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public KEYBDINPUT key; [FieldOffset(0)] public MOUSEINPUT mouse; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int x,y; public uint data, flags, time; public IntPtr extra; }
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] input, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    public static bool OwnsForeground(int expected) { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid == (uint)expected; }
    static INPUT Key(ushort vk, ushort scan, uint flags) { return new INPUT { type=1, data=new UNION { key=new KEYBDINPUT { vk=vk, scan=scan, flags=flags } } }; }
    static void Send(INPUT[] input) { if (SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT))) != input.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "Native keyboard input was not fully admitted."); }
    public static void Chord(ushort modifier, ushort key) { Send(new[] { Key(modifier,0,0), Key(key,0,0), Key(key,0,2), Key(modifier,0,2) }); }
    public static async Task<string> ReadBoundedAsync(StreamReader reader, int limit) { var buffer=new char[4096]; var text=new StringBuilder(); int count; while((count=await reader.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false))>0) { if(text.Length+count>limit) throw new IOException("Owned app stream exceeded declared one-MiB character bound."); text.Append(buffer,0,count); } return text.ToString(); }
    public static void TypeText(string text) { var input=new INPUT[text.Length*2]; for(int i=0;i<text.Length;i++) { input[i*2]=Key(0,text[i],4); input[i*2+1]=Key(0,text[i],6); } Send(input); }
}
'@
    $result.stage = 'fresh-default-profile'
    Check ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('HAVEN_DATA_DIR'))) 'Ordinary application data selection: no HAVEN_DATA_DIR override'
    $appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    Check (-not [string]::IsNullOrWhiteSpace($appData) -and [IO.Path]::IsPathRooted($appData)) 'Actual current Windows application-data folder is available'
    $havenRoot = Join-Path $appData 'Haven'
    Check (-not (Test-Path -LiteralPath $havenRoot) -and -not (Test-Path -LiteralPath (Join-Path $appData 'LocalCode'))) 'Existing native profile/state refused: disposable default profile starts absent'
    Check (@(Get-Process -Name 'HavenOS.Present' -ErrorAction SilentlyContinue).Count -eq 0) 'No competing Present process exists in the receiving runner'
    $documentsRoot = Join-Path $havenRoot 'Present\Documents'
    $exe = Join-Path $install $catalog.executable
    $result.profile = [ordered]@{ selection = 'Unmodified current Windows ApplicationData/Haven'; overrideUsed = $false; existingRootAbsent = $true; storedBodiesUploaded = $false }
    $result.stage = 'actual-native-create-edit-save'
    $window = Start-Present 'first-launch'
    [void](Wait-Observed { @(Document-Files).Count -eq 1 } 'Actual package created its first default presentation')
    $firstFile = @(Document-Files)[0].FullName
    $first = Read-Document $firstFile
    Check ($first.schemaVersion -eq 3 -and [Guid]$first.id -ne [Guid]::Empty -and $first.version -ge 1 -and @($first.slides).Count -eq 1) 'Application-generated canonical initial identity/schema/version/slide'
    $firstHash = (Get-FileHash -LiteralPath $firstFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $title = Wait-Edit $window 'Present.Deck.Title' 'Actual native title editor ready'
    [void](Observe-Edit $title 'Presentation title')
    Check ($null -eq (Find-Control $window 'Present.Slide.Title') -and $null -eq (Find-Control $window 'Present.Slide.Body')) 'Collapsed compatibility editor mirrors are absent from actual accessibility tree'
    File-Action $window 'New presentation'
    [void](Wait-Observed { @(Document-Files).Count -eq 2 } 'Actual File/New presentation created a second canonical document')
    $newFiles = @(Document-Files | Where-Object { $_.FullName -cne $firstFile })
    Check ($newFiles.Count -eq 1) 'Native New action produced one distinct local presentation'
    $documentPath = $newFiles[0].FullName
    $created = Read-Document $documentPath
    $identity = $created.id
    Check ([Guid]$identity -ne [Guid]::Empty -and $identity -cne $first.id -and $created.schemaVersion -eq 3 -and $created.version -ge 1) 'Native user-visible creation has a fresh persisted identity/schema/version'
    Check ((Get-FileHash -LiteralPath $firstFile -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $firstHash) 'New presentation preserves the earlier application-created document bytes'
    $title = Wait-Edit $window 'Present.Deck.Title' 'Native newly-created title'
    [void](Observe-Edit $title 'Presentation title')
    $editedTitle = 'Team C original packaged presentation'
    $editedNotes = 'Notes saved with actual Windows keyboard input'
    $result.expectedEdits = [ordered]@{ title = $editedTitle; speakerNotes = $editedNotes }
    Type-Text $title $editedTitle
    $add = Wait-Observed { Find-Control $window 'Present.Slide.Add' } 'Visible native Add slide control'
    Invoke-Button $add '+ Add slide'
    [void](Wait-Observed { @(Rail-Buttons $window).Count -eq 2 } 'Actual native slide rail contains two slide identities')
    $notes = Wait-Edit $window 'Present.Slide.Notes' 'Visible native notes editor'
    [void](Observe-Edit $notes 'Speaker notes')
    Type-Text $notes $editedNotes
    File-Action $window 'Save'
    [void](Wait-Observed {
        try { $doc = Read-Document $documentPath; $doc.title -ceq $editedTitle -and @($doc.slides).Count -eq 2 -and $doc.slides[1].speakerNotes -ceq $editedNotes } catch { $false }
    } 'Actual file save persists native title, added slide and notes')
    $saved = Read-Document $documentPath
    $slideId = $saved.slides[1].id
    Check (Check-Document $documentPath $identity $editedTitle $editedNotes $slideId) 'Durable native edits preserve original document and added-slide identities'
    $savedHash = (Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $result.document = [ordered]@{ id = $identity; schemaVersion = 3; slideId = $slideId; slideCount = 2; savedVersion = $saved.version; savedSha256 = $savedHash }
    $focusRefused = $false
    try { Assert-OwnedFocus ([System.Windows.Automation.AutomationElement]::RootElement) } catch { $focusRefused = $true }
    Check $focusRefused 'Keyboard guard refuses the real OS desktop root before sending any input'
    Focus-Edit $notes
    Capture-Window 'edited-window.png'
    $result.stage = 'actual-native-cancel-refusal'
    File-Action $window 'Open / import PowerPoint'
    $pidCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
    $dialog = Wait-Observed {
        $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $pidCondition)
        @($windows | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $_.Current.Name -ceq 'Open or import presentation' }) | Select-Object -First 1
    } 'Actual source-declared native import picker'
    Check ($dialog.Current.ProcessId -eq $process.Id -and $dialog.Current.IsEnabled -and -not $dialog.Current.IsOffscreen) 'Actual import picker is visible and owned by the exact packaged process'
    $dialog.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    [void](Wait-Observed { $process.Refresh(); $process.MainWindowHandle -ne [IntPtr]::Zero -and (Find-Control $window 'Present.Deck.Title').Current.IsEnabled } 'Native editor is usable after cancelling the picker')
    Check ((Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $savedHash -and @(Document-Files).Count -eq 2) 'Native picker cancellation creates no document and preserves saved bytes'
    Close-Present
    Check ((Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $savedHash) 'Normal native close preserves saved document bytes'
    $result.stage = 'actual-native-reopen-and-library-open'
    $window = Start-Present 'reopen'
    $title = Wait-Edit $window 'Present.Deck.Title' 'Reopened native title editor'
    $titleValue = Observe-Edit $title 'Presentation title'
    Check ($titleValue.Current.Value -ceq $editedTitle) 'Normal process reopen displays the saved native presentation title'
    $railId = 'Present.Rail.' + ([Guid]$slideId).ToString('N')
    $slide = Wait-Observed { Find-Control $window $railId } 'Reopened native added-slide selector'
    Invoke-Button $slide 'Slide 2: Slide 2'
    $notes = Wait-Edit $window 'Present.Slide.Notes' 'Reopened native notes editor'
    $notesValue = Observe-Edit $notes 'Speaker notes'
    Check ($notesValue.Current.Value -ceq $editedNotes -and @(Rail-Buttons $window).Count -eq 2) 'Native process reopen displays saved notes and both canonical slides'
    Check (Check-Document $documentPath $identity $editedTitle $editedNotes $slideId) 'Real native reopen preserves durable document, added-slide and notes identity'
    Check ((Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $savedHash) 'Reopened editor has not rewritten existing saved bytes'
    File-Action $window 'Back to presentations'
    $search = Wait-Edit $window 'Present.Library.Search' 'Native presentation library search editor'
    [void](Observe-Edit $search 'Search local presentation titles')
    Type-Text $search $editedTitle
    $openId = 'Present.Library.Open.' + ([Guid]$identity).ToString('N')
    $open = Wait-Observed { Find-Control $window $openId } 'Actual library exposes the saved presentation identity'
    $otherId = 'Present.Library.Open.' + ([Guid]$first.id).ToString('N')
    Check ($null -eq (Find-Control $window $otherId)) 'Real library title filter excludes the unmatched local document'
    Invoke-Button $open $editedTitle
    $title = Wait-Edit $window 'Present.Deck.Title' 'Actual library-selected document title'
    $titleValue = Observe-Edit $title 'Presentation title'
    Check ($titleValue.Current.Value -ceq $editedTitle) 'Native library Open displays the exact saved presentation title'
    $slide = Wait-Observed { Find-Control $window $railId } 'Library-opened added-slide selector'
    Invoke-Button $slide 'Slide 2: Slide 2'
    $notes = Wait-Edit $window 'Present.Slide.Notes' 'Library-opened speaker notes'
    $notesValue = Observe-Edit $notes 'Speaker notes'
    Check ($notesValue.Current.Value -ceq $editedNotes) 'Actual native library Open restores saved speaker notes'
    Focus-Edit $title; Capture-Window 'reopened-window.png'
    Close-Present
    Check (Check-Document $documentPath $identity $editedTitle $editedNotes $slideId) 'Final normal native close preserves canonical document and slide data'
    Check ((Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $savedHash -and (Get-FileHash -LiteralPath $firstFile -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $firstHash) 'Native save/open/reopen preserves both existing document byte identities'
    $result.document.finalSha256 = (Get-FileHash -LiteralPath $documentPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $publicFiles = @(Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -ceq 'result.json' -or $_.Extension -cin @('.png', '.log') })
    $publicBytes = [long]0; foreach ($file in $publicFiles) { $publicBytes += $file.Length }
    Check ($publicBytes -le 31457280) 'Selected public result/log/screenshot output fits the thirty-MiB budget'
    $result.publicOutputBytesBeforeFinalReceipt = $publicBytes
    $result.status = 'BOUNDED_PACKAGED_NATIVE_UI_CONTROLS_PASS_UNACCEPTED'; $result.stage = 'complete'; $exitCode = 0
} catch {
    $result.status = 'FAILED_OR_BLOCKED_UNACCEPTED'
    $result.failure = [ordered]@{ type = $_.Exception.GetType().FullName; stage = $result.stage; scriptLine = $_.InvocationInfo.ScriptLineNumber; rawExceptionText = 'WITHHELD' }
} finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                $result.cleanupCloseRequested = $process.CloseMainWindow()
                if (-not $process.WaitForExit(5000)) { $process.Kill(); $result.forcedCleanup = $true; if (-not $process.WaitForExit(5000)) { $result.cleanupFailure = 'Own packaged process remained alive after bounded forced cleanup.' } }
            }
            $result.cleanupExited = $process.HasExited
            if ($process.HasExited) {
                $result.cleanupExitCode = $process.ExitCode
                if (-not (Drain-OwnedLogs)) { $result.cleanupDrainFailure = 'Own-process stream drain incomplete or failed within bounded wait.' }
            }
        } catch { $result.cleanupFailure = $_.Exception.GetType().FullName } finally { $process.Dispose() }
    }
    Apply-Final-Failure-Guard
    try { Write-Result } catch { $exitCode = 1; Apply-Final-Failure-Guard; Write-Error ('Final evidence write failed; original caught failure: ' + ($result.failure | ConvertTo-Json -Compress)) -ErrorAction Continue }
}
exit $exitCode
