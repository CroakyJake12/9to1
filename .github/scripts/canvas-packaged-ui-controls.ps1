[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PackageInput,
      [Parameter(Mandatory=$true)][string]$ObservationInput,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$catalog=Get-Content -Raw -LiteralPath (Join-Path $repo '.github/validation/canvas-packaged-ui-controls.json') | ConvertFrom-Json
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Evidence directory must be new.'}
if($output.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Keep evidence outside source checkout.'}
[void][IO.Directory]::CreateDirectory($output)
$result=[ordered]@{schemaVersion=1;status='NOT_RUN';stage='preflight';checks=@();launches=@();screenshots=@();streamDrains=@();canvasStates=@();requests=@();forcedCleanup=$false;failure=$null;acceptanceVerified=$false;fullAppAcceptance='NOT_RUN';installedHomeTuple='NOT_RUN';cleanPcDelivery='NOT_RUN';package=$catalog.package;originalArtifacts=$catalog.artifacts;packageHashVerified=$false;manifestHashVerified=$false;extractedFilesVerified=$false;qualification='Actual original immutable packaged Canvas on a private SDK-present hosted Windows runner. Actual OS local profile, Files setup and embedded same-process Home one-request workflow only; no installed Home cross-app tuple, donor matrix, signing, account/ACL policy, clean-PC or full-app acceptance.'}
$process=$null;$exitCode=1;$mouseDown=$false
$controlledName='Team C native Canvas stroke'
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
    $result.observationTimeout = [ordered]@{ name = $Name; seconds = $Seconds }
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
function Close-Canvas {
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
    Check ([CanvasPackageInput]::OwnsForeground($process.Id)) 'Screenshot is bound to the actual app foreground process'
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

function Read-BoundedJson([string]$Path,[long]$MaximumBytes=8388608) {
    $full=[IO.Path]::GetFullPath($Path)
    if((Get-Item -LiteralPath $full).Length -gt $MaximumBytes -or ([IO.File]::GetAttributes($full) -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Owner-produced JSON is oversized or redirected.'}
    return Get-Content -Raw -LiteralPath $full | ConvertFrom-Json
}
function Require-OwnedControl($Element,[string]$Role) {
    if($null -eq $Element){throw 'Required native control is absent.'}
    $c=$Element.Current;$r=$c.BoundingRectangle
    Check ($c.ProcessId -eq $process.Id -and $c.ControlType.ProgrammaticName -ceq $Role) 'Native control has the exact owned process and role'
    Check ($c.IsEnabled -and -not $c.IsOffscreen -and $r.Width -gt 0 -and $r.Height -gt 0) 'Native control is enabled and visible with real geometry'
    $work=[Windows.Forms.Screen]::FromHandle($process.MainWindowHandle).WorkingArea
    Check ($r.Left -ge $work.Left -and $r.Top -ge $work.Top -and $r.Right -le $work.Right -and $r.Bottom -le $work.Bottom) 'Native control fits wholly inside actual monitor work area'
    Check (-not $process.HasExited -and [CanvasPackageInput]::OwnsForeground($process.Id)) 'Native control action belongs to exact live foreground app'
}
function Find-Unique([string]$Locator,[string]$Role,[bool]$ById=$true,$Root=$window) {
    $property=if($ById){[System.Windows.Automation.AutomationElement]::AutomationIdProperty}else{[System.Windows.Automation.AutomationElement]::NameProperty}
    $condition=New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($property,$Locator)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)))
    $matches=@($Root.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object {$_.Current.ControlType.ProgrammaticName -ceq $Role})
    if($matches.Count -gt 1){throw 'Ambiguous native control in actual owned app.'}
    if($matches.Count -eq 1){return $matches[0]};return $null
}
function Observe-Control([string]$Locator,[string]$Role,[bool]$ById=$true) {
    $element=Wait-Observed {Find-Unique $Locator $Role $ById} "Actual native control: $Locator"
    Require-OwnedControl $element $Role
    return $element
}
function Invoke-Button([string]$ButtonName,[bool]$ById=$false) {
    $button=Observe-Control $ButtonName 'ControlType.Button' $ById
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Check $true "Actual native InvokePattern admitted $ButtonName"
}
function Type-Edit($Editor,[string]$Text) {
    Require-OwnedControl $Editor 'ControlType.Edit';$Editor.SetFocus()
    $focus=[System.Windows.Automation.AutomationElement]::FocusedElement
    Check ($focus.Current.ProcessId -eq $process.Id -and $Editor.Current.HasKeyboardFocus) 'Native keyboard target is exact owned focused editor'
    [CanvasPackageInput]::Chord(0x11,0x41);[CanvasPackageInput]::TypeText($Text)
    [void](Wait-Observed {$Editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ceq $Text} 'Actual typed native editor value')
}
function Position-Window {
    Check ([CanvasPackageInput]::SetForegroundWindow($process.MainWindowHandle)) 'Actual app foreground activation admitted'
    Check ([CanvasPackageInput]::ForegroundMatches($process.MainWindowHandle)) 'Window positioning targets exact app HWND'
    [CanvasPackageInput]::Chord(0x5B,0x26)
    [void](Wait-Observed {$r=$window.Current.BoundingRectangle;$w=[Windows.Forms.Screen]::FromHandle($process.MainWindowHandle).WorkingArea;$r.Width -gt 0 -and $r.Height -gt 0 -and [Math]::Max(0,[Math]::Min($r.Right,$w.Right)-[Math]::Max($r.Left,$w.Left))*[Math]::Max(0,[Math]::Min($r.Bottom,$w.Bottom)-[Math]::Max($r.Top,$w.Top))-ge 0.95*$r.Width*$r.Height} 'Actual owned app fits real monitor work area')
}
function Start-Canvas([string]$LaunchLabel) {
    $start=New-Object Diagnostics.ProcessStartInfo;$start.FileName=$exe;$start.WorkingDirectory=$install
    $start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $script:process=New-Object Diagnostics.Process;$process.StartInfo=$start
    if(-not $process.Start()){throw 'Original packaged Canvas did not start.'}
    $script:stdout=$process.StandardOutput.ReadToEndAsync();$script:stderr=$process.StandardError.ReadToEndAsync();$script:launchLabel=$LaunchLabel
    $script:launchRecord=[ordered]@{label=$LaunchLabel;processId=$process.Id;windowObserved=$false;closeRequested=$false;exited=$false;exitCode=$null}
    $result.launches+=$launchRecord;Write-Result
    [void](Wait-Observed {$process.Refresh();if($process.HasExited){throw 'Canvas exited before native window.'};$process.MainWindowHandle -ne [IntPtr]::Zero} 'Actual packaged Canvas HWND')
    $window=[System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    Check ($window.Current.ProcessId -eq $process.Id -and $window.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) 'Exact packaged Canvas HWND exposes native Window'
    $launchRecord.windowObserved=$true;$launchRecord.windowTitle=$window.Current.Name;return $window
}
function Choose-OwnFolder {
    Invoke-Button 'Set up Canvases'
    $dialog=Wait-Observed {
        $condition=New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)),
            (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,'Choose an empty Files workspace folder')))
        $found=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$condition)
        if($found.Count -gt 1){throw 'Ambiguous actual native folder picker.'};if($found.Count -eq 1){$found[0]}
    } 'Actual source-declared native folder picker'
    $handle=[IntPtr]$dialog.Current.NativeWindowHandle
    Check ([CanvasPackageInput]::SetForegroundWindow($handle) -and [CanvasPackageInput]::ForegroundMatches($handle)) 'Native folder address input targets exact owned picker HWND'
    [CanvasPackageInput]::Chord(0x11,0x4C)
    $address=Wait-Observed {$f=[System.Windows.Automation.AutomationElement]::FocusedElement;if($f.Current.ProcessId -eq $process.Id -and $f.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit){$f}} 'Actual focused OS folder-address Edit'
    Require-OwnedControl $address 'ControlType.Edit'
    Check (Is-InSurface $address $dialog) 'Actual focused folder-address editor belongs to exact native picker tree'
    [CanvasPackageInput]::TypeText($filesRoot)
    Check ($address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ceq $filesRoot) 'Actual picker typed exact private empty fixture path'
    [CanvasPackageInput]::Press(0x0D)
    [void](Wait-Observed {
        $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
        $witness=@($dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ToolBar -and -not $_.Current.IsOffscreen -and $_.Current.Name.EndsWith($filesRoot,[StringComparison]::OrdinalIgnoreCase)})
        try{$addressGone=$address.Current.IsOffscreen}catch{$addressGone=$true}
        $addressGone -and $witness.Count -eq 1
    } 'Actual native picker navigated breadcrumb to exact private fixture')
    $defaults=@();$condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
    foreach($candidate in $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition)){
        if($candidate.Current.ControlType -ne [System.Windows.Automation.ControlType]::Button){continue}
        try{$legacy=$candidate.GetCurrentPattern([System.Windows.Automation.LegacyIAccessiblePattern]::Pattern);if(($legacy.Current.State -band 0x100)-ne 0){$defaults+=$candidate}}catch{}
    }
    Check ($defaults.Count -eq 1) 'Actual native folder picker exposes one observed standard default button'
    Require-OwnedControl $defaults[0] 'ControlType.Button'
    Check ([CanvasPackageInput]::ForegroundMatches($handle)) 'Folder choice remains on exact owned picker HWND'
    $defaults[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    [void](Wait-Observed {Test-Path -LiteralPath (Join-Path $filesRoot '.9to1-files/drive.json') -PathType Leaf} 'Actual Files owner created private workspace after native picker choice')
}
function Read-CurrentCanvas {
    $home=Read-BoundedJson $homePath
    $profiles=@($home.records | Where-Object {$_.recordId -ceq 'home.local-profile'})
    $configs=@($home.records | Where-Object {$_.recordType -ceq 'files.native-workspace'})
    if($profiles.Count -ne 1 -or $configs.Count -ne 1 -or [Guid]$profiles[0].payload.ProfileId -ne $privateProfileId -or [Guid]$configs[0].payload.ProfileId -ne $privateProfileId -or $configs[0].payload.RootDirectory -cne $filesRoot){throw 'Actual original private profile/Files configuration changed.'}
    $canvasFolder=[Guid]$configs[0].payload.AppFolders.canvas.value
    $drive=Read-BoundedJson (Join-Path $filesRoot '.9to1-files/drive.json')
    if($drive.schemaVersion -ne 1){throw 'Unsupported actual Files envelope.'};$state=$drive.state
    $refs=@($state.artifacts | Where-Object {$_.ownerAppId -ceq 'canvas' -and $_.displayName -ceq ($controlledName+'.9to1c')})
    if($refs.Count -ne 1){throw 'Expected one actual owner-created Canvas reference.'};$reference=$refs[0];$id=[Guid]$reference.fileId.value
    $revisions=@($state.revisions | Where-Object {$_.isCurrent -and [Guid]$_.itemId.value -eq $id})
    if($revisions.Count -ne 1){throw 'Expected one actual current Canvas Files revision.'};$revision=$revisions[0]
    $items=@($state.items | Where-Object {-not $_.deleted -and [Guid]$_.metadata.id.value -eq $id})
    if($items.Count -ne 1 -or [Guid]$items[0].metadata.currentRevisionId.value -ne [Guid]$revision.id.value -or [Guid]$items[0].metadata.parentId.value -ne $canvasFolder -or [Guid]$reference.parentFolderId.value -ne $canvasFolder){throw 'Actual current Files metadata differs from original canonical Canvas folder/revision.'}
    $key=([Guid]$revision.id.value).ToString('N');$relative=$state.revisionContentReferences.$key
    $folder=Join-Path $filesRoot 'Canvas';$path=[IO.Path]::GetFullPath((Join-Path $folder $relative))
    if(-not $path.StartsWith($folder+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Actual content reference escapes private canonical Canvas folder.'}
    for($part=$path;$null -ne $part -and $part.StartsWith($filesRoot,[StringComparison]::OrdinalIgnoreCase);$part=[IO.Path]::GetDirectoryName($part)){if(([IO.File]::GetAttributes($part)-band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Actual content path is redirected.'}}
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();$bytes=(Get-Item -LiteralPath $path).Length
    $doc=Read-BoundedJson $path
    if($doc.format -cne '9to1.Canvas' -or $doc.schemaVersion -ne 2 -or $revision.owningAppId -cne 'canvas' -or $revision.contentHash.ToLowerInvariant() -cne $hash -or $revision.sizeBytes -ne $bytes -or [Guid]$reference.artifactId -ne [Guid]$doc.artifact.artifactId -or [Guid]$revision.owningAppRevisionId -ne [Guid]$doc.artifact.revisionId){throw 'Owner-produced Canvas/Files bytes and identity bindings disagree.'}
    return @{document=$doc;fileId=$id.ToString('D');filesRevision=([Guid]$revision.id.value).ToString('D');hash=$hash;bytes=$bytes}
}
function Record-CanvasState([string]$Label,[int]$StrokeCount) {
    $current=Read-CurrentCanvas;$a=$current.document.artifact;$page=$a.pages[0]
    Check (@($page.strokes).Count -eq $StrokeCount -and @($page.strokeOrder).Count -eq $StrokeCount) 'Actual canonical stroke count and ordering match observed native workflow'
    $result.canvasStates+=[ordered]@{label=$Label;artifactId=$a.artifactId;revisionId=$a.revisionId;pageId=$page.pageId;fileId=$current.fileId;filesRevision=$current.filesRevision;sha256=$current.hash;bytes=$current.bytes;strokeCount=$StrokeCount}
    Write-Result;return $current
}
function Accept-OwnRequest([string]$ExpectedPreview) {
    $preview=Observe-Control 'approval-preview' 'ControlType.Text'
    Check ($preview.Current.Name -ceq $ExpectedPreview) 'Actual displayed Home request is the exact bounded task operation'
    [void](Observe-Control 'Pending requests: 1' 'ControlType.Text' $false)
    [void](Observe-Control 'Trusted access grants: 0' 'ControlType.Text' $false)
    $result.requests+=[ordered]@{preview=$preview.Current.Name;approval='Actual native Home Accept once';retainedTrust=$false;processId=$process.Id};Write-Result
    Invoke-Button 'approval-accept' $true
    Invoke-Button 'Finish approved request'
}
function Is-InSurface($Element,$Surface) {
    $expected=$Surface.GetRuntimeId();$walker=[System.Windows.Automation.TreeWalker]::RawViewWalker
    for($depth=0;$depth -lt 32 -and $null -ne $Element;$depth++){
        if(([string]::Join(',', $Element.GetRuntimeId()) -ceq [string]::Join(',', $expected))){return $true};$Element=$walker.GetParent($Element)
    };return $false
}
function Draw-OwnStroke($Surface) {
    Require-OwnedControl $Surface 'ControlType.Custom';$r=$Surface.Current.BoundingRectangle
    Check ($r.Width -ge 150 -and $r.Height -ge 100) 'Actual native drawing surface admits bounded interior stroke geometry'
    $screen=[Windows.Forms.SystemInformation]::VirtualScreen
    $points=@(@([int]($r.X+$r.Width*0.25),[int]($r.Y+$r.Height*0.3)),@([int]($r.X+$r.Width*0.35),[int]($r.Y+$r.Height*0.32)),@([int]($r.X+$r.Width*0.45),[int]($r.Y+$r.Height*0.34)))
    try {
        for($i=0;$i -lt $points.Count;$i++){
            $x=$points[$i][0];$y=$points[$i][1]
            Check ([CanvasPackageInput]::OwnsForeground($process.Id) -and [CanvasPackageInput]::OwnsPoint($x,$y,$process.Id) -and (Is-InSurface ([System.Windows.Automation.AutomationElement]::FromPoint((New-Object System.Windows.Point($x,$y)))) $Surface)) 'Each native mouse point belongs to exact visible owned drawing surface'
            [CanvasPackageInput]::Move($x,$y,$screen.X,$screen.Y,$screen.Width,$screen.Height)
            if($i -eq 0){[CanvasPackageInput]::MouseButton($true,$process.Id);$script:mouseDown=$true}
            Start-Sleep -Milliseconds 100
        }
    } finally {
        if($mouseDown){
            if(-not [CanvasPackageInput]::OwnsForeground($process.Id)){throw 'Mouse release refused outside exact foreground app.'}
            [CanvasPackageInput]::MouseButton($false,$process.Id);$script:mouseDown=$false
        }
    }
    Check $true 'Real bounded native pointer stroke was released to existing owner request flow'
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
    Check ($manifest.workflowCommit -ceq $catalog.sourceBasis -and $manifest.target -ceq 'canvas' -and $manifest.package.sha256 -ceq $catalog.package.sha256) 'Original package manifest binds Canvas and exact producer'
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
public static class CanvasPackageInput {
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public KEYBDINPUT key; [FieldOffset(0)] public MOUSEINPUT mouse; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int x,y; public uint data, flags, time; public IntPtr extra; }
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint count, INPUT[] input, int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    public static bool OwnsForeground(int expected) { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid == (uint)expected; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x,y; public POINT(int a,int b) {x=a;y=b;} }
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    public static bool ForegroundMatches(IntPtr expected) { return GetForegroundWindow()==expected; }
    public static bool OwnsPoint(int x,int y,int expected) { uint pid; GetWindowThreadProcessId(WindowFromPoint(new POINT(x,y)),out pid); return pid==(uint)expected; }
    public static void Press(ushort key) { Send(new[] { Key(key,0,0),Key(key,0,2) }); }
    public static void Move(int x,int y,int sx,int sy,int sw,int sh) {
        if(sw<=1 || sh<=1 || x<sx || y<sy || x>=sx+sw || y>=sy+sh) throw new ArgumentOutOfRangeException("pointer", "Native pointer must fit actual virtual screen bounds.");
        var dx=(int)Math.Round((x-sx)*65535.0/(sw-1)); var dy=(int)Math.Round((y-sy)*65535.0/(sh-1));
        Send(new[] {new INPUT { type=0,data=new UNION {mouse=new MOUSEINPUT {x=dx,y=dy,flags=0xC001}}}});
    }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    public static void MouseButton(bool down,int expected) { POINT point;
        if(!OwnsForeground(expected) || !GetCursorPos(out point) || !OwnsPoint(point.x,point.y,expected)) throw new InvalidOperationException("Mouse transition refused outside exact foreground owned process.");
        Send(new[] {new INPUT {type=0,data=new UNION {mouse=new MOUSEINPUT {flags=down ? 2u : 4u}}}}); }
    static INPUT Key(ushort vk, ushort scan, uint flags) { return new INPUT { type=1, data=new UNION { key=new KEYBDINPUT { vk=vk, scan=scan, flags=flags } } }; }
    static void Send(INPUT[] input) { if (SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT))) != input.Length) throw new Win32Exception(Marshal.GetLastWin32Error(), "Native keyboard input was not fully admitted."); }
    public static void Chord(ushort modifier, ushort key) { Send(new[] { Key(modifier,0,0), Key(key,0,0), Key(key,0,2), Key(modifier,0,2) }); }
    public static void TypeText(string text) { var input=new INPUT[text.Length*2]; for(int i=0;i<text.Length;i++) { input[i*2]=Key(0,text[i],4); input[i*2+1]=Key(0,text[i],6); } Send(input); }
}
'@

    $exe=Join-Path $install $catalog.executable
    $local=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    Check (-not [string]::IsNullOrWhiteSpace($local)) 'Actual OS local application profile location is available'
    $homePath=Join-Path $local '9to1/Home/home-core-state.json'
    Check (-not (Test-Path -LiteralPath $homePath) -and -not (Test-Path -LiteralPath ($homePath+'.bak'))) 'Private runner starts without existing Home profile state or approvals'
    $filesRoot=Join-Path $output 'runtime-fixture';[void][IO.Directory]::CreateDirectory($filesRoot)
    Check (@(Get-ChildItem -LiteralPath $filesRoot -Force).Count -eq 0) 'Own explicitly selected Files fixture starts empty'
    $result.stage='actual-native-files-setup';$window=Start-Canvas 'first-launch';Position-Window
    Choose-OwnFolder
    $home=Read-BoundedJson $homePath;$profiles=@($home.records | Where-Object {$_.recordId -ceq 'home.local-profile'})
    Check ($profiles.Count -eq 1 -and [Guid]$profiles[0].payload.ProfileId -ne [Guid]::Empty) 'Actual OS-backed canonical Home profile exists after native initialization'
    $privateProfileId=[Guid]$profiles[0].payload.ProfileId
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent();$sha=[Security.Cryptography.SHA256]::Create()
    try{$principalBytes=[Text.Encoding]::UTF8.GetBytes('windows-sid:'+$identity.User.Value);$principalDigest=([BitConverter]::ToString($sha.ComputeHash($principalBytes))).Replace('-','')}
    finally{$identity.Dispose();$sha.Dispose()}
    Check ($profiles[0].payload.PrincipalDigest -ceq $principalDigest) 'Actual private Home profile binds the original OS process principal'
    $profileBytes=[Text.Encoding]::UTF8.GetBytes([string]$profiles[0].payload.ProfileId);$sha=[Security.Cryptography.SHA256]::Create()
    try{$result.privateProfileIdentitySha256=([BitConverter]::ToString($sha.ComputeHash($profileBytes))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
    $configs=@($home.records | Where-Object {$_.recordType -ceq 'files.native-workspace'})
    Check ($configs.Count -eq 1 -and $configs[0].payload.RootDirectory -ceq $filesRoot) 'Real native picker configured exactly the owned empty Files directory'
    $result.stage='actual-native-create';$name=Observe-Control 'canvas-host-new-name' 'ControlType.Edit';Type-Edit $name $controlledName
    Invoke-Button 'Create canvas';Accept-OwnRequest 'Create this new editable Canvas in the configured Files folder'
    [void](Wait-Observed {try{(Read-CurrentCanvas).document.artifact.displayName -ceq $controlledName}catch{$false}} 'Actual native create returned durable canonical Files Canvas')
    [void](Observe-Control 'canvas-title' 'ControlType.Text');$baseline=Record-CanvasState 'created-blank' 0
    $surface=Observe-Control 'canvas-surface' 'ControlType.Custom';Capture-Window 'canvas-created-window.png'
    $result.stage='actual-native-stroke';Invoke-Button 'canvas-tool-pen' $true
    $surface=Observe-Control 'canvas-surface' 'ControlType.Custom';Draw-OwnStroke $surface
    $preview=Observe-Control 'approval-preview' 'ControlType.Text'
    Check ($preview.Current.Name -ceq 'Add this exact captured stroke to the displayed Canvas revision' -and (Read-CurrentCanvas).filesRevision -ceq $baseline.filesRevision) 'Actual unapproved native stroke preserves previously committed Files revision'
    Accept-OwnRequest 'Add this exact captured stroke to the displayed Canvas revision'
    [void](Wait-Observed {try{(Read-CurrentCanvas).filesRevision -cne $baseline.filesRevision}catch{$false}} 'Actual approved native stroke produced a new durable Files revision')
    $edited=Record-CanvasState 'stroke-committed' 1;$a=$edited.document.artifact;$stroke=$a.pages[0].strokes[0]
    $native=$a.documentSettings.properties.'9to1.Canvas.RnoteState'
    $nativeBytes=[Convert]::FromBase64String($native.PayloadBase64);$sha=[Security.Cryptography.SHA256]::Create()
    try{$nativeHash=([BitConverter]::ToString($sha.ComputeHash($nativeBytes))).Replace('-','')}finally{$sha.Dispose()}
    Check ($native.SchemaVersion -eq 1 -and $native.Sha256 -ceq $nativeHash) 'Actual retained native stroke snapshot has its own exact owner-produced hash' 
    Check ($a.artifactId -ceq $baseline.document.artifact.artifactId -and $a.pages[0].pageId -ceq $baseline.document.artifact.pages[0].pageId -and $a.revisionId -cne $baseline.document.artifact.revisionId -and $edited.hash -cne $baseline.hash -and [Guid]$stroke.strokeId -eq [Guid]$a.pages[0].strokeOrder[0] -and @($stroke.pathGeometry.segments).Count -gt 0 -and @($native.nativeStrokeKeys.PSObject.Properties).Count -eq 1 -and $null -ne $native.nativeStrokeKeys.([string]$stroke.strokeId)) 'Real committed stroke preserves canonical identities and structured/native stroke binding'
    [void](Observe-Control 'canvas-surface' 'ControlType.Custom');Capture-Window 'canvas-stroke-window.png';Close-Canvas
    $result.stage='actual-native-reopen';$window=Start-Canvas 'reopen';Position-Window
    Invoke-Button 'Refresh Canvases';Invoke-Button 'Open selected'
    $title=Observe-Control 'canvas-title' 'ControlType.Text';Check ($title.Current.Name -ceq $controlledName) 'Actual reopened native Canvas displays controlled name'
    [void](Observe-Control 'canvas-surface' 'ControlType.Custom');$reopened=Record-CanvasState 'native-reopened' 1
    Check ($reopened.filesRevision -ceq $edited.filesRevision -and $reopened.hash -ceq $edited.hash -and $reopened.document.artifact.artifactId -ceq $a.artifactId -and $reopened.document.artifact.revisionId -ceq $a.revisionId) 'Actual native reopen retains exact committed Files/app identity and bytes'
    Capture-Window 'canvas-reopened-window.png';Close-Canvas
    $result.status='BOUNDED_PACKAGED_NATIVE_CANVAS_CONTROLS_PASS_UNACCEPTED';$result.stage='complete';$exitCode=0
} catch {
    # Private Home/profile JSON and external exception text never enter public observations.
    # Exact controlled Check/Wait witnesses remain in checks/observationTimeout and stage.
    $result.status='FAILED_OR_BLOCKED_UNACCEPTED';$result.failure=[ordered]@{type=$_.Exception.GetType().FullName;stage=$result.stage;rawExceptionText='WITHHELD'}
    if($null -ne $process){try{Capture-Window 'canvas-original-failure-window.png'}catch{$result.failureScreenshotError=$_.Exception.GetType().FullName}}
} finally {
    if($mouseDown){$result.mouseReleaseFailure='Own native gesture did not release normally.';$result.status='FAILED_OR_BLOCKED_UNACCEPTED';$exitCode=1}
    if($null -ne $process){
        try{
            if(-not $process.HasExited){[void]$process.CloseMainWindow();if(-not $process.WaitForExit(5000)){$process.Kill();$result.forcedCleanup=$true;[void]$process.WaitForExit(5000)}}
            if($process.HasExited){if(-not (Drain-OwnedLogs)){$result.cleanupDrainFailure='Own streams did not complete bounded drain.'}}
        }catch{$result.cleanupFailure=$_.Exception.GetType().FullName}finally{$process.Dispose()}
    }
    if($result.forcedCleanup -or $result.Contains('cleanupDrainFailure') -or $result.Contains('cleanupFailure')){$result.status='FAILED_OR_BLOCKED_UNACCEPTED';$exitCode=1}
    Write-Result
}
exit $exitCode
