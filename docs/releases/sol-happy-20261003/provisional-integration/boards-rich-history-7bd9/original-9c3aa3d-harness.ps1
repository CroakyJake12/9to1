[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageInput,
    [Parameter(Mandatory = $true)][string]$ObservationInput,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateSet('edit-save', 'rich-history')][string]$Scenario = 'edit-save'
)

# Windows PowerShell 5.1 / standard Windows UIAutomation and keyboard input.
# No native app test hook, model/API write, SDK build or Home bootstrap.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$catalog = Get-Content -Raw -LiteralPath (Join-Path $repo '.github/validation/boards-packaged-ui-controls.json') | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Evidence directory must be new.' }
if ($output.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep runtime evidence outside the source checkout.' }
[void][IO.Directory]::CreateDirectory($output)
$result = [ordered]@{
    schemaVersion = 1; status = 'NOT_RUN'; stage = 'preflight'; scenario = $Scenario; checks = @(); launches = @(); screenshots = @(); streamDrains = @()
    producerRunId = $catalog.producerRunId; producerCommit = $catalog.sourceBasis
    originalArtifacts = $catalog.artifacts; package = $catalog.package
    packageHashVerified = $false; manifestHashVerified = $false; extractedFilesVerified = $false
    forcedCleanup = $false; acceptanceVerified = $false; fullAppAcceptance = 'NOT_RUN'
    installedHomeTuple = 'NOT_RUN'; cleanPcDelivery = 'NOT_RUN'; failure = $null
    qualification = 'Actual immutable packaged executable on a disposable SDK-present hosted Windows runner. Bounded native UI, keyboard and persistence controls only; no Home installed tuple, signing, donor matrix, Android, clean-PC or full-app acceptance.'
}
$process = $null
$exitCode = 1

function Write-Result { $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding UTF8 }
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
    throw "Timed out observing: $Name"
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
    if ($process.HasExited -or -not [BoardsPackageInput]::OwnsForeground($process.Id)) { throw 'Keyboard input refused outside the exact app foreground process.' }
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($null -eq $focused -or $focused.Current.ProcessId -ne $process.Id -or
        $focused.Current.AutomationId -cne $Element.Current.AutomationId -or -not $Element.Current.HasKeyboardFocus) {
        throw 'Keyboard input refused without the exact native control focus.'
    }
}
function Focus-Edit([System.Windows.Automation.AutomationElement]$Element) {
    Check ([BoardsPackageInput]::SetForegroundWindow($process.MainWindowHandle)) 'Native app admitted foreground activation'
    $Element.SetFocus()
    [void](Wait-Observed { $Element.Current.HasKeyboardFocus -and [BoardsPackageInput]::OwnsForeground($process.Id) } 'Exact native editor focus')
    Assert-OwnedFocus $Element
}
function Type-Text([System.Windows.Automation.AutomationElement]$Element, [string]$Text) {
    Focus-Edit $Element
    Assert-OwnedFocus $Element
    [BoardsPackageInput]::Chord(0x11, 0x41) # Ctrl+A
    Assert-OwnedFocus $Element
    [BoardsPackageInput]::TypeText($Text)
    $value = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    [void](Wait-Observed { $value.Current.Value -ceq $Text } 'Real native keyboard text value')
    Check ($value.Current.Value -ceq $Text) 'Actual keyboard input updated the native editor value'
}
function Read-Board { return Get-Content -Raw -LiteralPath $boardPath | ConvertFrom-Json }
function Find-NamedButton([System.Windows.Automation.AutomationElement]$Window, [string]$Name) {
    $namedButton = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
    $condition = New-Object System.Windows.Automation.AndCondition(
        $namedButton,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)))
    $matches = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($matches.Count -gt 1) { throw "Ambiguous actual native button: $Name" }
    if ($matches.Count -eq 0) { return $null }
    return $matches[0]
}
function Observe-NamedButton([string]$Name) {
    $button = Wait-Observed { Find-NamedButton $window $Name } "Actual named native button: $Name"
    $c = $button.Current
    Check ($c.ProcessId -eq $process.Id -and $c.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $c.Name -ceq $Name) "Exact process-bound native Button: $Name"
    Check ($c.IsEnabled -and -not $c.IsOffscreen -and $c.BoundingRectangle.Width -gt 0 -and $c.BoundingRectangle.Height -gt 0) "Enabled visible native Button: $Name"
    return $button
}
function Native-BoldIs([bool]$Expected) {
    # Formatting/history rebuild peers. Always read the current native peer.
    try {
        $button = Find-NamedButton $window 'Bold'
        if ($null -eq $button) { return $false }
        $pattern = $button.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
        $state = if ($Expected) { [System.Windows.Automation.ToggleState]::On } else { [System.Windows.Automation.ToggleState]::Off }
        return $pattern.Current.ToggleState -eq $state
    } catch [System.Windows.Automation.ElementNotAvailableException] { return $false }
}
function Durable-BoldIs([bool]$Expected) {
    try {
        $doc = Read-Board
        $blocks = @($doc.richNotes.sections[0].pages[0].blocks | Where-Object { $_.id -ceq $paragraphs[0].id })
        if ($doc.documentId -cne $identity -or $blocks.Count -ne 1 -or $blocks[0].plainText -cne $historyText) { return $false }
        $runs = @($blocks[0].runs)
        if ($Expected) { return $runs.Count -eq 1 -and $runs[0].bold -and $runs[0].text -ceq $historyText }
        return @($runs | Where-Object { $_.bold }).Count -eq 0
    } catch { return $false }
}
function Observe-HistoryState([string]$Label, [bool]$Bold) {
    [void](Wait-Observed { (Native-BoldIs $Bold) -and (Durable-BoldIs $Bold) } "Native and durable canonical rich state: $Label")
    $button = Observe-NamedButton 'Bold'
    $toggle = $button.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    Check ((Native-BoldIs $Bold) -and (Durable-BoldIs $Bold)) "Actual $Label preserves paragraph text/block/document identity and expected canonical bold"
    $doc = Read-Board
    $result.historyStates += [ordered]@{ label = $Label; bold = $Bold; nativeToggleState = $toggle.Current.ToggleState.ToString(); documentId = $doc.documentId; paragraphId = $paragraphs[0].id; canonicalVersion = $doc.richNotes.version; fileSha256 = (Get-FileHash -LiteralPath $boardPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    Write-Result
}
function Invoke-NativeHistory([string]$Name) {
    $button = Observe-NamedButton $Name
    Check (-not $process.HasExited -and [BoardsPackageInput]::OwnsForeground($process.Id)) "Native $Name invocation belongs to the exact foreground app"
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Check $true "Actual native $Name InvokePattern admitted the app command"
}
function Start-Board([string]$Label) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $exe; $start.Arguments = '"' + $boardPath + '"'; $start.WorkingDirectory = $install
    $start.UseShellExecute = $false; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $script:process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    if (-not $process.Start()) { throw 'The actual packaged executable did not start.' }
    $script:stdout = $process.StandardOutput.ReadToEndAsync()
    $script:stderr = $process.StandardError.ReadToEndAsync()
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
function Close-Board {
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
    Check ([BoardsPackageInput]::OwnsForeground($process.Id)) 'Screenshot is bound to the actual app foreground process'
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $r = $window.Current.BoundingRectangle
    $bounds = New-Object Drawing.Rectangle([int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    $visible = [Drawing.Rectangle]::Intersect($bounds, [Windows.Forms.SystemInformation]::VirtualScreen)
    Check ($visible.Width -gt 0 -and $visible.Height -gt 0 -and $visible.Width -le 7680 -and $visible.Height -le 4320) 'Actual app screenshot has bounded visible screen geometry'
    $bitmap = New-Object Drawing.Bitmap($visible.Width, $visible.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($visible.Location, [Drawing.Point]::Empty, $visible.Size)
        $bitmap.Save((Join-Path $output $Name), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    $image = Join-Path $output $Name
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
    $result.stage = 'immutable-package-custody'
    $zip = Find-InputFile $PackageInput $catalog.package.file
    Check ((Get-Item -LiteralPath $zip).Length -eq $catalog.package.bytes -and (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $catalog.package.sha256) 'Exact original inner package bytes and SHA256'
    $result.packageHashVerified = $true
    $manifestPath = Find-InputFile $ObservationInput 'result.json'
    Check ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $catalog.originalResultSha256) 'Exact original producer manifest SHA256'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    Check ($manifest.workflowCommit -ceq $catalog.sourceBasis -and $manifest.target -ceq 'boards' -and $manifest.package.sha256 -ceq $catalog.package.sha256) 'Original package manifest binds Boards and exact producer'
    $result.manifestHashVerified = $true
    Add-Type -AssemblyName System.IO.Compression.FileSystem, UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
    $install = Join-Path $output 'install'
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $archive.Entries) {
            $full = [IO.Path]::GetFullPath((Join-Path $install $entry.FullName))
            if (-not $full.StartsWith($install + '\', [StringComparison]::OrdinalIgnoreCase) -or
                ($entry.ExternalAttributes -band 1024) -ne 0 -or (($entry.ExternalAttributes -shr 16) -band 61440) -eq 40960) { throw 'Unsafe package archive entry.' }
        }
    } finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $install)
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
using System.Runtime.InteropServices;
public static class BoardsPackageInput {
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
    public static void TypeText(string text) { var input=new INPUT[text.Length*2]; for(int i=0;i<text.Length;i++) { input[i*2]=Key(0,text[i],4); input[i*2+1]=Key(0,text[i],6); } Send(input); }
}
'@
    $result.stage = if ($Scenario -ceq 'rich-history') { 'actual-native-rich-history' } else { 'actual-native-edit-save-reopen' }
    $exe = Join-Path $install $catalog.executable
    $fixture = Join-Path $output 'runtime-fixture'; [void][IO.Directory]::CreateDirectory($fixture)
    $boardPath = Join-Path $fixture 'Packaged UI board.9to1board'
    Check (-not (Test-Path -LiteralPath $boardPath) -and -not $boardPath.Contains('"')) 'Explicit fixture path starts absent and is safely quoted'
    $window = Start-Board 'first-launch'
    [void](Wait-Observed { Test-Path -LiteralPath $boardPath -PathType Leaf } 'Board file created by the actual packaged application')
    $initial = Read-Board
    Check ($initial.format -ceq '9to1.board' -and $initial.schemaVersion -eq 3 -and [Guid]$initial.documentId -ne [Guid]::Empty) 'Application created a genuine canonical board identity and schema'
    $identity = $initial.documentId
    $paragraphs = @($initial.richNotes.sections[0].pages[0].blocks | Where-Object { $_.kind -eq 0 })
    Check ($paragraphs.Count -eq 1) 'Fresh actual application has one canonical paragraph target'
    $paragraphId = 't_' + $paragraphs[0].id
    if ($Scenario -ceq 'rich-history') {
        $result.historyStates = @()
        $historyText = 'Rich history formatted through native Windows input'
        $result.expectedRichHistory = [ordered]@{ paragraph = $historyText; finalBold = $true; historyInput = 'Actual application Undo and Redo buttons via UIAutomation InvokePattern' }
        $paragraph = Wait-Observed { Find-Control $window $paragraphId } 'Actual selected paragraph derived from app-created identity'
        [void](Observe-Edit $paragraph 'Paragraph editor')
        Type-Text $paragraph $historyText
        Assert-OwnedFocus $paragraph; [BoardsPackageInput]::Chord(0x11, 0x53)
        Observe-HistoryState 'plain-baseline' $false
        $paragraph = Wait-Observed { Find-Control $window $paragraphId } 'Current paragraph before focused Ctrl+B'
        Focus-Edit $paragraph
        Assert-OwnedFocus $paragraph; [BoardsPackageInput]::Chord(0x11, 0x42) # Ctrl+B
        Check $true 'Actual focused Ctrl+B admitted existing selected-paragraph format command'
        Observe-HistoryState 'formatted' $true
        Invoke-NativeHistory 'Undo'
        Observe-HistoryState 'undone' $false
        Invoke-NativeHistory 'Redo'
        Observe-HistoryState 'redone' $true
        $paragraph = Wait-Observed { Find-Control $window $paragraphId } 'Current paragraph after native history rebuilds'
        Focus-Edit $paragraph
        Assert-OwnedFocus $paragraph; [BoardsPackageInput]::Chord(0x11, 0x53)
        Check $true 'Actual Ctrl+S admitted after native rich-history round trip'
        Capture-Window 'rich-history-edited-window.png'
        Close-Board
        $beforeReopen = (Get-FileHash -LiteralPath $boardPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $result.stage = 'actual-native-formatted-reopen'
        $window = Start-Board 'reopen'
        $paragraph = Wait-Observed { Find-Control $window $paragraphId } 'Real reopened paragraph with same application-generated block identity'
        $paragraphValue = Observe-Edit $paragraph 'Paragraph editor'
        Check ($paragraphValue.Current.Value -ceq $historyText) 'Real native reopen restores rich-history paragraph text'
        Focus-Edit $paragraph
        Observe-HistoryState 'reopened-formatted' $true
        Check ((Get-FileHash -LiteralPath $boardPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $beforeReopen) 'Formatted native reopen preserves saved file bytes before close'
        Capture-Window 'rich-history-reopened-window.png'
        Close-Board
        Check (Durable-BoldIs $true) 'Final native close retains actual formatted paragraph and canonical identities'
    } else {
    $title = Wait-Observed { Find-Control $window 'BoardTitleBox' } 'Actual native board title editor'
    $paragraph = Wait-Observed { Find-Control $window $paragraphId } 'Actual native paragraph editor derived from real document identity'
    $search = Wait-Observed { Find-Control $window 'NavSearchBox' } 'Actual native navigation search editor'
    [void](Observe-Edit $title 'Board title'); [void](Observe-Edit $paragraph 'Paragraph editor'); [void](Observe-Edit $search 'Search sections and pages')
    $editedTitle = 'Team C packaged native edit'
    $editedParagraph = 'Saved from actual Windows keyboard input'
    $result.expectedEdits = [ordered]@{ title = $editedTitle; paragraph = $editedParagraph }
    Type-Text $title $editedTitle
    Type-Text $paragraph $editedParagraph
    Assert-OwnedFocus $paragraph; [BoardsPackageInput]::Chord(0x11, 0x53) # Ctrl+S
    Check $true 'Ctrl+S keyboard input admitted only to the actual focused app'
    [void](Wait-Observed {
        try { $doc = Read-Board; $block = @($doc.richNotes.sections[0].pages[0].blocks | Where-Object { $_.id -ceq $paragraphs[0].id }); $doc.richNotes.title -ceq $editedTitle -and $block.Count -eq 1 -and $block[0].plainText -ceq $editedParagraph } catch { $false }
    } 'Durable file reflects actual native title and paragraph edits')
    Check ((Read-Board).documentId -ceq $identity) 'Native editing and save preserve the application-generated Board identity'
    Capture-Window 'edited-window.png'
    Close-Board
    $beforeReopen = (Get-FileHash -LiteralPath $boardPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $result.stage = 'actual-native-reopen'
    $window = Start-Board 'reopen'
    $title = Wait-Observed { Find-Control $window 'BoardTitleBox' } 'Reopened native title'
    $paragraph = Wait-Observed { Find-Control $window $paragraphId } 'Reopened native paragraph'
    $titleValue = Observe-Edit $title 'Board title'; $paragraphValue = Observe-Edit $paragraph 'Paragraph editor'
    Check ($titleValue.Current.Value -ceq $editedTitle -and $paragraphValue.Current.Value -ceq $editedParagraph) 'Real native reopen displays saved title and paragraph'
    Check ((Read-Board).documentId -ceq $identity -and (Get-FileHash -LiteralPath $boardPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $beforeReopen) 'Native reopen preserves document identity and existing file bytes before close'
    Focus-Edit $title; Capture-Window 'reopened-window.png'
    Close-Board
    Check ((Read-Board).documentId -ceq $identity) 'Final native close preserves Board identity'
    }
    $result.document = [ordered]@{ documentId = $identity; schemaVersion = $initial.schemaVersion; paragraphId = $paragraphs[0].id; finalSha256 = (Get-FileHash -LiteralPath $boardPath -Algorithm SHA256).Hash.ToLowerInvariant() }
    $result.status = 'BOUNDED_PACKAGED_NATIVE_UI_CONTROLS_PASS_UNACCEPTED'; $result.stage = 'complete'; $exitCode = 0
} catch {
    $result.status = 'FAILED_OR_BLOCKED_UNACCEPTED'
    $result.failure = [ordered]@{ type = $_.Exception.GetType().FullName; message = $_.Exception.Message }
} finally {
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) {
                [void]$process.CloseMainWindow()
                if (-not $process.WaitForExit(5000)) { $process.Kill(); $result.forcedCleanup = $true; [void]$process.WaitForExit(5000) }
            }
            if ($process.HasExited) {
                if (-not (Drain-OwnedLogs)) { $result.cleanupDrainFailure = 'Own-process stream drain incomplete or failed within bounded wait.' }
            }
        } catch { $result.cleanupFailure = $_.Exception.GetType().FullName } finally { $process.Dispose() }
    }
    Write-Result
}
exit $exitCode
