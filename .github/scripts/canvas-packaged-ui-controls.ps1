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
function Test-OwnedPickerSnapshot($Snapshot) {
    return $Snapshot.foregroundProcessId -eq $process.Id -and $Snapshot.nativeTitleMatches -and
        $Snapshot.ownerChainMatches -and $Snapshot.uiaProcessId -eq $process.Id -and
        $Snapshot.uiaRole -ceq 'ControlType.Window' -and $Snapshot.uiaTitleMatches -and
        $Snapshot.uiaWindowHandle -eq $Snapshot.foregroundWindowHandle -and
        $Snapshot.foregroundWindowHandle -ne 0
}
function Record-OwnedPickerTreeWitness {
    $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
    $witness=[ordered]@{}
    foreach($entry in @(@{label='rootChildren';root=[System.Windows.Automation.AutomationElement]::RootElement},@{label='ownedMainWindowChildren';root=$window})) {
        $roles=[ordered]@{Window=0;Button=0;Edit=0;Text=0;Custom=0;Other=0};$titleCount=0;$count=0
        foreach($peer in $entry.root.FindAll([System.Windows.Automation.TreeScope]::Children,$condition)) {
            if($count -ge 64){throw 'Owned picker tree witness exceeds bounded child count.'}
            $count++;$role=$peer.Current.ControlType.ProgrammaticName.Replace('ControlType.','')
            if($roles.Contains($role)){$roles[$role]++}else{$roles.Other++}
            if($peer.Current.Name -ceq 'Choose an empty Files workspace folder'){$titleCount++}
        }
        $witness[$entry.label]=[ordered]@{ownProcessChildCount=$count;exactSourceTitleCount=$titleCount;roles=$roles}
    }
    $result.pickerTreeWitness=$witness;Write-Result
}
function Find-OwnedForegroundPicker([bool]$Cleanup=$false) {
    $handle=[CanvasPackageInput]::ForegroundHandle();$expectedMain=[IntPtr]$window.Current.NativeWindowHandle
    $snapshot=[ordered]@{foregroundWindowHandle=$handle.ToInt64();foregroundProcessId=[CanvasPackageInput]::WindowPid($handle);nativeTitleMatches=$false;nativeWindowClass='WITHHELD';ownerChain=@();ownerChainMatches=$false;uiaProcessId=0;uiaWindowHandle=0;uiaRole='UNOBSERVED';uiaTitleMatches=$false;uiaVisible=$false;uiaEnabled=$false;uiaReadFailure=$null}
    if($snapshot.foregroundProcessId -eq $process.Id) {
        $snapshot.nativeTitleMatches=[CanvasPackageInput]::WindowTitleEquals($handle,'Choose an empty Files workspace folder')
        $snapshot.nativeWindowClass=[CanvasPackageInput]::WindowClass($handle)
        $owner=[CanvasPackageInput]::OwnerHandle($handle)
        for($depth=0;$owner -ne [IntPtr]::Zero -and $depth -lt 8;$depth++) {
            $ownerPid=[CanvasPackageInput]::WindowPid($owner)
            $snapshot.ownerChain+=[ordered]@{windowHandle=$owner.ToInt64();processId=$ownerPid;isOriginalMainWindow=$owner -eq $expectedMain}
            if($ownerPid -ne $process.Id){break}
            if($owner -eq $expectedMain){$snapshot.ownerChainMatches=$true;break}
            $owner=[CanvasPackageInput]::OwnerHandle($owner)
        }
        try {
            $peer=[System.Windows.Automation.AutomationElement]::FromHandle($handle);$current=$peer.Current
            $snapshot.uiaProcessId=$current.ProcessId;$snapshot.uiaWindowHandle=[long]$current.NativeWindowHandle
            $snapshot.uiaRole=$current.ControlType.ProgrammaticName;$snapshot.uiaTitleMatches=$current.Name -ceq 'Choose an empty Files workspace folder'
            $snapshot.uiaVisible=-not $current.IsOffscreen;$snapshot.uiaEnabled=$current.IsEnabled
        }catch{$snapshot.uiaReadFailure=$_.Exception.GetType().FullName}
    }
    if($Cleanup){$result.pickerCleanupObservation=$snapshot}else{$result.pickerObservation=$snapshot};Write-Result
    if(Test-OwnedPickerSnapshot $snapshot){return $peer};return $null
}
function Observe-OwnedPicker {
    try {$dialog=Wait-Observed {Find-OwnedForegroundPicker} 'Actual source-declared native folder picker'}
    catch {
        $originalPickerFailure=$_
        try{Record-OwnedPickerTreeWitness}catch{$result.pickerWitnessFailure=$_.Exception.GetType().FullName}
        throw $originalPickerFailure
    }
    Require-OwnedControl $dialog 'ControlType.Window'
    Check ([CanvasPackageInput]::ForegroundMatches([IntPtr]$dialog.Current.NativeWindowHandle)) 'Native picker observation binds exact current owned foreground HWND'
    Record-OwnedPickerTreeWitness;return $dialog
}
function Read-PickerPeerWitness($Peer,[IntPtr]$PickerHandle,[IntPtr]$KernelHandle=[IntPtr]::Zero) {
    $w=[ordered]@{kernelWindowHandle=$KernelHandle.ToInt64();kernelProcessId=0;kernelClass='UNOBSERVED';kernelIsPickerChild=$false;uiaProcessId=0;uiaWindowHandle=0;uiaHandleIsZero=$true;uiaMatchesKernelHandle=$false;uiaKernelProcessId=0;uiaIsPickerChild=$false;role='UNOBSERVED';enabled=$false;offscreen=$true;rectangle=$null;nameMatchesCancel=$false;nameMatchesSelectFolder=$false;nativeCaptionMatchesCancel=$false;nativeCaptionMatchesSelectFolder=$false;valuePattern=$false;invokePattern=$false;legacyTypeAvailable=$false;legacyPattern=$null;legacyDefaultButton=$null;legacyReadStatus='NOT_READ';readPhase='properties';readFailure=$null}
    if($KernelHandle -ne [IntPtr]::Zero) {
        $w.kernelProcessId=[CanvasPackageInput]::WindowPid($KernelHandle)
        $w.kernelIsPickerChild=[CanvasPackageInput]::IsChild($PickerHandle,$KernelHandle)
        if($w.kernelProcessId -eq $process.Id){$w.kernelClass=[CanvasPackageInput]::WindowClass($KernelHandle);$w.nativeCaptionMatchesCancel=[CanvasPackageInput]::WindowTitleEquals($KernelHandle,'Cancel');$w.nativeCaptionMatchesSelectFolder=[CanvasPackageInput]::WindowTitleEquals($KernelHandle,'Select Folder')}
    }
    try {
        if($null -eq $Peer){throw 'No observed native UIA peer.'};$c=$Peer.Current;$r=$c.BoundingRectangle
        $w.uiaProcessId=$c.ProcessId;$w.uiaWindowHandle=[long]$c.NativeWindowHandle;$w.uiaHandleIsZero=$w.uiaWindowHandle -eq 0
        $w.uiaMatchesKernelHandle=$KernelHandle -ne [IntPtr]::Zero -and $w.uiaWindowHandle -eq $KernelHandle.ToInt64()
        if(-not $w.uiaHandleIsZero){$w.uiaKernelProcessId=[CanvasPackageInput]::WindowPid([IntPtr]$w.uiaWindowHandle);$w.uiaIsPickerChild=[CanvasPackageInput]::IsChild($PickerHandle,[IntPtr]$w.uiaWindowHandle)}
        $w.role=$c.ControlType.ProgrammaticName;$w.enabled=$c.IsEnabled;$w.offscreen=$c.IsOffscreen;$w.rectangle=@($r.X,$r.Y,$r.Width,$r.Height)
        $w.nameMatchesCancel=$c.Name -ceq 'Cancel';$w.nameMatchesSelectFolder=$c.Name -ceq 'Select Folder'
        $w.readPhase='value-pattern';$pattern=$null;$w.valuePattern=$Peer.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern,[ref]$pattern)
        $w.readPhase='invoke-pattern';$pattern=$null;$w.invokePattern=$Peer.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern,[ref]$pattern)
        $w.readPhase='legacy-type-availability';$legacyType='System.Windows.Automation.LegacyIAccessiblePattern' -as [type];$w.legacyTypeAvailable=$null -ne $legacyType
        if($w.legacyTypeAvailable){
            $w.readPhase='legacy-pattern';$pattern=$null;$w.legacyPattern=$Peer.TryGetCurrentPattern($legacyType::Pattern,[ref]$pattern)
            if($w.legacyPattern){$w.legacyDefaultButton=($pattern.Current.State -band 0x100)-ne 0};$w.legacyReadStatus='OBSERVED'
        }else{$w.legacyReadStatus='MANAGED_TYPE_UNAVAILABLE'}
        $w.readPhase='completed'
    }catch{$w.readFailure=$_.Exception.GetType().FullName}
    return $w
}
function Record-PickerControlWitness($Dialog,[string]$Phase) {
    $handle=[IntPtr]$Dialog.Current.NativeWindowHandle
    $snapshot=[ordered]@{phase=$Phase;completed=$false;foregroundMatchesPicker=[CanvasPackageInput]::ForegroundMatches($handle);pickerKernelProcessId=[CanvasPackageInput]::WindowPid($handle);globalUiaFocus=$null;kernelFocus=$null;uiaDescendants=@();nativeDescendants=@();nativeEnumerationStoppedAtBound=$false}
    if($Phase -ceq 'focused-address-timeout'){$result.pickerFocusedControlWitness=$snapshot}else{$result.pickerCancelControlWitness=$snapshot}
    $focus=[System.Windows.Automation.AutomationElement]::FocusedElement
    $snapshot.globalUiaFocus=Read-PickerPeerWitness $focus $handle
    $kernelFocus=[CanvasPackageInput]::FocusHandle($handle,$process.Id)
    $peer=$null
    if($kernelFocus -ne [IntPtr]::Zero -and [CanvasPackageInput]::WindowPid($kernelFocus) -eq $process.Id){try{$peer=[System.Windows.Automation.AutomationElement]::FromHandle($kernelFocus)}catch{}}
    $snapshot.kernelFocus=Read-PickerPeerWitness $peer $handle $kernelFocus
    $count=0
    foreach($peer in $Dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)) {
        if($count -ge 128){throw 'Picker UIA diagnostic exceeds bounded descendant count.'};$count++
        $snapshot.uiaDescendants+=Read-PickerPeerWitness $peer $handle
    }
    $children=[CanvasPackageInput]::ChildHandles($handle);$snapshot.nativeEnumerationStoppedAtBound=$children.Length -ge 128
    foreach($child in $children) {
        $peer=$null
        if([CanvasPackageInput]::WindowPid($child) -eq $process.Id){try{$peer=[System.Windows.Automation.AutomationElement]::FromHandle($child)}catch{}}
        $snapshot.nativeDescendants+=Read-PickerPeerWitness $peer $handle $child
    }
    $snapshot.completed=$true
    Write-Result
}
function Cancel-OwnPickerOnFailure {
    $dialog=Find-OwnedForegroundPicker $true
    if($null -eq $dialog){return}
    Require-OwnedControl $dialog 'ControlType.Window'
    $handle=[IntPtr]$dialog.Current.NativeWindowHandle
    try{$cancel=Find-Unique 'Cancel' 'ControlType.Button' $false $dialog;Require-OwnedControl $cancel 'ControlType.Button'}
    catch{
        $originalCancelFailure=$_
        try{Record-PickerControlWitness $dialog 'cancel-refusal'}catch{$result.pickerCancelWitnessFailure=$_.Exception.GetType().FullName}
        throw $originalCancelFailure
    }
    Check ((Is-InSurface $cancel $dialog) -and [CanvasPackageInput]::ForegroundMatches($handle)) 'Failure Cancel targets exact proven owned native picker'
    $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $result.pickerCancellation=[ordered]@{admitted=$true;windowHandle=$handle.ToInt64();closed=$false};Write-Result
    [void](Wait-Observed {-not [CanvasPackageInput]::WindowExists($handle)} 'Actual owned failed picker closed after controlled Cancel')
    $result.pickerCancellation.closed=$true;Write-Result
}
function Test-GenuineUiAutomationAssembly($Snapshot,[string]$ExpectedName,[string]$ExpectedVersion,[string]$ExpectedCulture) {
    return $Snapshot.name -ceq $ExpectedName -and $Snapshot.version -ceq $ExpectedVersion -and $Snapshot.culture -ceq $ExpectedCulture -and $Snapshot.publicKeyToken -ceq '31bf3856ad364e35' -and $Snapshot.globalAssemblyCache -and $Snapshot.beneathWindowsDirectory -and -not [string]::IsNullOrWhiteSpace($Snapshot.location) -and $Snapshot.sha256 -cmatch '^[0-9a-f]{64}$'
}
function Read-UiAutomationAssembly($Assembly) {
    $name=$Assembly.GetName();$location=[IO.Path]::GetFullPath($Assembly.Location)
    $windows=[IO.Path]::GetFullPath([Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)).TrimEnd('\')+'\'
    $beneathWindows=$location.StartsWith($windows,[StringComparison]::OrdinalIgnoreCase);$publicLocation='WITHHELD_NON_SYSTEM';if($beneathWindows -and $Assembly.GlobalAssemblyCache){$publicLocation=$location}
    return [ordered]@{name=$name.Name;version=$name.Version.ToString();culture=$name.CultureName;publicKeyToken=([BitConverter]::ToString($name.GetPublicKeyToken())).Replace('-','').ToLowerInvariant();globalAssemblyCache=$Assembly.GlobalAssemblyCache;beneathWindowsDirectory=$beneathWindows;location=$publicLocation;sha256=(Get-FileHash -LiteralPath $location -Algorithm SHA256).Hash.ToLowerInvariant()}
}
function Read-BoundedProviderExceptionTypes([Exception]$Failure) {
    $chain=@();$current=$Failure
    while($null -ne $current -and $chain.Count -lt 8){
        $chain+=[ordered]@{type=$current.GetType().FullName;hresult=$current.HResult};$current=$current.InnerException
    }
    return [ordered]@{chain=$chain;truncated=$null -ne $current;maximumEntries=8;rawMessage='WITHHELD';rawToString='WITHHELD'}
}
function Read-KnownProviderThrowFrames([Exception]$Failure) {
    $rows=@();$current=$Failure;$exceptionIndex=0;$truncated=$false
    while($null -ne $current -and $exceptionIndex -lt 8 -and $rows.Count -lt 16){
        # false excludes file/source paths. Only fixed known-method booleans are
        # retained, never arbitrary names, signatures, stack text or locations.
        $trace=New-Object -TypeName System.Diagnostics.StackTrace -ArgumentList $current,$false
        for($i=0;$i -lt $trace.FrameCount -and $rows.Count -lt 16;$i++){
            $method=$trace.GetFrame($i).GetMethod();$reflected=$null;$declaring=$null;$methodName=$null
            if($null -ne $method){$reflected=$method.ReflectedType;$declaring=$method.DeclaringType;$methodName=$method.Name}
            $proxy=$null -ne $declaring -and $declaring.FullName -ceq 'MS.Internal.Automation.ProxyManager'
            $clientSettings=$null -ne $declaring -and $declaring.FullName -ceq 'System.Windows.Automation.ClientSettings'
            $rows+=[ordered]@{exceptionIndex=$exceptionIndex;methodIsNull=$null -eq $method;reflectedTypeIsNull=$null -eq $reflected;declaringTypeIsNull=$null -eq $declaring;isProxyManager=$proxy;isLoadDefaultProxies=$proxy -and $methodName -ceq 'LoadDefaultProxies';isRegisterWindowHandlers=$proxy -and $methodName -ceq 'RegisterWindowHandlers';isRegisterProxyAssembly=$proxy -and $methodName -ceq 'RegisterProxyAssembly';isPublicClientSettingsRegisterAssembly=$clientSettings -and $methodName -ceq 'RegisterClientSideProviderAssembly';isTypedHarnessClient=$null -ne $declaring -and $declaring.FullName -ceq 'CanvasStandardUiAutomationProviderClient'}
        }
        if($i -lt $trace.FrameCount){$truncated=$true}
        $current=$current.InnerException;$exceptionIndex++
    }
    return [ordered]@{frames=$rows;maximumFrames=16;maximumExceptions=8;truncated=$truncated -or $null -ne $current;rawStack='WITHHELD';sourcePaths='WITHHELD'}
}
function Record-StandardProviderRefusalWitness($Provider,[Exception]$Failure) {
    $w=[ordered]@{registrationException=Read-BoundedProviderExceptionTypes $Failure;canonicalTypePresent=$false;canonicalTypeIsPublic=$false;publicStaticTablePresent=$false;publicStaticTableExpectedType=$false;tableReadAttempted=$false;tableReadCompleted=$false;tableIsNull=$null;tableCount=$null;publicTableReadFailure=$null;knownThrowFrames=$null;throwFrameReadFailure=$null}
    $result.uiaProviderSetup.registrationFailureWitness=$w
    try{$w.knownThrowFrames=Read-KnownProviderThrowFrames $Failure}catch{$w.throwFrameReadFailure=Read-BoundedProviderExceptionTypes $_.Exception}
    try{
        # These are the exact canonical type/field used by the public framework
        # registration implementation. No private members/table bodies are read.
        $type=$Provider.GetType($Provider.GetName().Name+'.UIAutomationClientSideProviders')
        $w.canonicalTypePresent=$null -ne $type
        if($null -ne $type){
            $w.canonicalTypeIsPublic=$type.IsPublic
            $field=$type.GetField('ClientSideProviderDescriptionTable',[Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
            $w.publicStaticTablePresent=$null -ne $field
            if($null -ne $field){
                $w.publicStaticTableExpectedType=$field.FieldType -eq [System.Windows.Automation.ClientSideProviderDescription[]]
                if($w.publicStaticTableExpectedType){
                    $w.tableReadAttempted=$true;$table=$field.GetValue($null);$w.tableReadCompleted=$true;$w.tableIsNull=$null -eq $table
                    if($null -ne $table){$w.tableCount=$table.Length}
                }
            }
        }
    }catch{$w.publicTableReadFailure=Read-BoundedProviderExceptionTypes $_.Exception}
    Write-Result
}
function Initialize-StandardUiAutomationProviders {
    $result.uiaProviderSetup=[ordered]@{registrationCompleted=$false;client=$null;provider=$null;managedLegacyTypeAvailable=$false}
    $client=[System.Windows.Automation.AutomationElement].Assembly;$clientName=$client.GetName()
    $clientSnapshot=Read-UiAutomationAssembly $client;$result.uiaProviderSetup.client=$clientSnapshot
    Check (Test-GenuineUiAutomationAssembly $clientSnapshot 'UIAutomationClient' $clientName.Version.ToString() $clientName.CultureName) 'Actual UIAutomation client is genuine Microsoft installed matching framework assembly'
    $requested=New-Object System.Reflection.AssemblyName
    $requested.Name='UIAutomationClientsideProviders';$requested.Version=$clientName.Version;$requested.CultureInfo=$clientName.CultureInfo;$requested.SetPublicKeyToken($clientName.GetPublicKeyToken())
    $provider=[System.Reflection.Assembly]::Load($requested)
    $providerSnapshot=Read-UiAutomationAssembly $provider;$result.uiaProviderSetup.provider=$providerSnapshot
    Check (Test-GenuineUiAutomationAssembly $providerSnapshot $requested.Name $clientName.Version.ToString() $clientName.CultureName) 'Actual standard UIAutomation provider is genuine installed Microsoft assembly matching loaded client'
    # Public framework API registers only the genuine installed OS client-side proxies.
    # Existing Edit/Value, Button/Invoke, PID, ownership and geometry criteria remain required.
    # The framework default-proxy loader walks caller ReflectedType metadata.
    # A genuine ordinary C# caller avoids a null PowerShell dynamic-method type,
    # without retrying, replacing providers or bypassing the public framework API.
    Add-Type -ReferencedAssemblies $client.Location -TypeDefinition @'
using System.Reflection;
using System.Runtime.CompilerServices;
public static class CanvasStandardUiAutomationProviderClient {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(AssemblyName assemblyName) {
        System.Windows.Automation.ClientSettings.RegisterClientSideProviderAssembly(assemblyName);
    }
}
'@
    $method=[CanvasStandardUiAutomationProviderClient].GetMethod('Register')
    $result.uiaProviderSetup.typedCallerNoInlining=($method.GetMethodImplementationFlags() -band [Reflection.MethodImplAttributes]::NoInlining)-ne 0
    Check $result.uiaProviderSetup.typedCallerNoInlining 'Actual standard provider client uses ordinary non-inlined typed public API caller'
    try{[CanvasStandardUiAutomationProviderClient]::Register($requested)}
    catch{
        $originalRegistrationFailure=$_
        try{Record-StandardProviderRefusalWitness $provider $originalRegistrationFailure.Exception}
        catch{$result.uiaProviderSetup.registrationWitnessFailure=Read-BoundedProviderExceptionTypes $_.Exception}
        throw $originalRegistrationFailure
    }
    $result.uiaProviderSetup.registrationCompleted=$true
    $result.uiaProviderSetup.managedLegacyTypeAvailable=$null -ne ('System.Windows.Automation.LegacyIAccessiblePattern' -as [type])
    Write-Result
}
function Test-OwnedDefaultButtonSnapshot($Snapshot) {
    return $Snapshot.nativeWindowHandle -ne 0 -and $Snapshot.nativeProcessId -eq $process.Id -and $Snapshot.nativeIsPickerChild -and $Snapshot.nativeClass -ceq 'Button' -and $Snapshot.nativeCaptionMatchesSelectFolder -and $Snapshot.uiaProcessId -eq $process.Id -and $Snapshot.uiaWindowHandle -eq $Snapshot.nativeWindowHandle -and $Snapshot.uiaRole -ceq 'ControlType.Button' -and $Snapshot.uiaNameMatchesSelectFolder
}
function Test-OwnedPickerValueBinding($Snapshot) {
    return $Snapshot.editorProcessId -eq $process.Id -and $Snapshot.focusProcessId -eq $process.Id -and $Snapshot.editorRole -ceq 'ControlType.Edit' -and $Snapshot.focusRole -ceq 'ControlType.Edit' -and $Snapshot.editorEnabled -and -not $Snapshot.editorOffscreen -and $Snapshot.editorWidth -gt 0 -and $Snapshot.editorHeight -gt 0 -and $Snapshot.focusSameEditor -and $Snapshot.editorInDialog -and $Snapshot.foregroundIsOwnedPicker -and $Snapshot.processLive -and $Snapshot.pickerProcessId -eq $process.Id -and $Snapshot.pickerRole -ceq 'ControlType.Window' -and $Snapshot.pickerHandleMatchesExpected
}
function Read-BoundedPickerValueWitness([string]$Value,[string]$Expected,[bool]$Diagnostics=$false) {
    $s=[ordered]@{exactValue=$false;valueLength=$null;expectedLength=$null;valueSha256=$null;expectedSha256=$null;readRefusal=$null}
    if($Diagnostics){$s.valueLength=$Value.Length;$s.expectedLength=$Expected.Length}
    if($Value.Length -gt 32768 -or $Expected.Length -gt 32768){$s.readRefusal='BoundedValueLengthRefusal';return $s}
    $s.exactValue=$Value -ceq $Expected
    if($Diagnostics){
        # Failure-only metadata never returns either private text value.
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$s.valueSha256=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)))).Replace('-','').ToLowerInvariant();$s.expectedSha256=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Expected)))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
    }
    return $s
}
function Read-BoundedRuntimeIdWitness([int[]]$Id) {
    $w=[ordered]@{observed=$null -ne $Id;length=$null;bounded=$false;sha256=$null;hashFormat='int32-little-endian';readRefusal=$null}
    if($null -eq $Id){return $w}
    $w.length=$Id.Length
    if($Id.Length -eq 0 -or $Id.Length -gt 32){$w.readRefusal='RuntimeIdLengthRefusal';return $w}
    $bytes=New-Object byte[] ($Id.Length*4)
    for($i=0;$i -lt $Id.Length;$i++){$part=[BitConverter]::GetBytes($Id[$i]);if(-not [BitConverter]::IsLittleEndian){[Array]::Reverse($part)};[Array]::Copy($part,0,$bytes,$i*4,4)}
    $sha=[Security.Cryptography.SHA256]::Create()
    try{$w.sha256=([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
    $w.bounded=$true
    return $w
}
function Read-CapturedPickerIdentityWitness($Editor,$Focus,$EditorCurrent,$FocusCurrent,$Dialog,[IntPtr]$PickerHandle,[bool]$OriginalEquals) {
    $w=[ordered]@{readPhase='owner-admission';completed=$false;originalInstanceEquals=$OriginalEquals;editorIsActualAutomationElement=$null;focusIsActualAutomationElement=$null;ownedCapturedPair=$false;editorNativeWindowHandle=$null;focusNativeWindowHandle=$null;sameNonzeroNativeHandle=$null;editorBounds=$null;focusBounds=$null;editorKernelProcessId=$null;focusKernelProcessId=$null;editorNativeIsPickerChild=$null;focusNativeIsPickerChild=$null;editorNativeClassIsEdit=$null;focusNativeClassIsEdit=$null;focusInDialog=$null;kernelFocusHandle=$null;kernelFocusProcessId=$null;kernelFocusIsPickerChild=$null;kernelFocusClassIsEdit=$null;kernelFocusMatchesEditor=$null;kernelFocusMatchesFocus=$null;typedCompareObserved=$false;typedCompareResult=$null;typedCompareFailureType=$null;editorRuntimeId=$null;focusRuntimeId=$null;runtimeCompareObserved=$false;runtimeIdsEqual=$null;readRefusal=$null;readFailureType=$null}
    try{
        $w.editorIsActualAutomationElement=$Editor -is [System.Windows.Automation.AutomationElement]
        $w.focusIsActualAutomationElement=$Focus -is [System.Windows.Automation.AutomationElement]
        $w.ownedCapturedPair=$w.editorIsActualAutomationElement -and $w.focusIsActualAutomationElement -and $EditorCurrent.ProcessId -eq $process.Id -and $FocusCurrent.ProcessId -eq $process.Id -and $EditorCurrent.ControlType.ProgrammaticName -ceq 'ControlType.Edit' -and $FocusCurrent.ControlType.ProgrammaticName -ceq 'ControlType.Edit' -and -not $process.HasExited -and $PickerHandle -ne [IntPtr]::Zero -and [CanvasPackageInput]::ForegroundMatches($PickerHandle) -and [CanvasPackageInput]::WindowPid($PickerHandle) -eq $process.Id
        if(-not $w.ownedCapturedPair){$w.readRefusal='CapturedPairOwnerOrRoleRefusal';return $w}
        # These are the SAME current structures and focus peer captured by the
        # original observation, not a replacement editor or fresh focus query.
        $w.readPhase='captured-native-metadata'
        $editorHandle=[IntPtr]$EditorCurrent.NativeWindowHandle;$focusHandle=[IntPtr]$FocusCurrent.NativeWindowHandle
        $w.editorNativeWindowHandle=$editorHandle.ToInt64();$w.focusNativeWindowHandle=$focusHandle.ToInt64()
        $w.sameNonzeroNativeHandle=$editorHandle -ne [IntPtr]::Zero -and $editorHandle -eq $focusHandle
        $er=$EditorCurrent.BoundingRectangle;$fr=$FocusCurrent.BoundingRectangle
        $w.editorBounds=[ordered]@{x=$er.X;y=$er.Y;width=$er.Width;height=$er.Height};$w.focusBounds=[ordered]@{x=$fr.X;y=$fr.Y;width=$fr.Width;height=$fr.Height}
        foreach($pair in @(@{prefix='editor';handle=$editorHandle},@{prefix='focus';handle=$focusHandle})){
            if($pair.handle -ne [IntPtr]::Zero){
                $nativePid=[CanvasPackageInput]::WindowPid($pair.handle);$w[$pair.prefix+'KernelProcessId']=$nativePid
                if($nativePid -eq $process.Id){$w[$pair.prefix+'NativeIsPickerChild']=[CanvasPackageInput]::IsChild($PickerHandle,$pair.handle);$w[$pair.prefix+'NativeClassIsEdit']=[CanvasPackageInput]::WindowClass($pair.handle) -ceq 'Edit'}
            }
        }
        $w.focusInDialog=Is-InSurface $Focus $Dialog
        $kernelFocus=[CanvasPackageInput]::FocusHandle($PickerHandle,$process.Id);$w.kernelFocusHandle=$kernelFocus.ToInt64()
        if($kernelFocus -ne [IntPtr]::Zero){
            $w.kernelFocusProcessId=[CanvasPackageInput]::WindowPid($kernelFocus)
            if($w.kernelFocusProcessId -eq $process.Id){$w.kernelFocusIsPickerChild=[CanvasPackageInput]::IsChild($PickerHandle,$kernelFocus);$w.kernelFocusClassIsEdit=[CanvasPackageInput]::WindowClass($kernelFocus) -ceq 'Edit';$w.kernelFocusMatchesEditor=$kernelFocus -eq $editorHandle;$w.kernelFocusMatchesFocus=$kernelFocus -eq $focusHandle}
        }
        $w.readPhase='typed-public-uia-compare'
        try{$w.typedCompareResult=[System.Windows.Automation.Automation]::Compare($Editor,$Focus);$w.typedCompareObserved=$true}catch{$w.typedCompareFailureType=$_.Exception.GetType().FullName}
        $w.readPhase='editor-runtime-id';[int[]]$editorId=$Editor.GetRuntimeId();$w.editorRuntimeId=Read-BoundedRuntimeIdWitness $editorId
        $w.readPhase='focus-runtime-id';[int[]]$focusId=$Focus.GetRuntimeId();$w.focusRuntimeId=Read-BoundedRuntimeIdWitness $focusId
        if($w.editorRuntimeId.bounded -and $w.focusRuntimeId.bounded){$w.readPhase='typed-public-runtime-id-compare';$w.runtimeIdsEqual=[System.Windows.Automation.Automation]::Compare($editorId,$focusId);$w.runtimeCompareObserved=$true}
        $w.readPhase='completed';$w.completed=$true
    }catch{$w.readFailureType=$_.Exception.GetType().FullName}
    return $w
}
function Read-OwnedPickerValueObservation($Editor,$Dialog,[IntPtr]$PickerHandle,[string]$Expected,[bool]$Diagnostics=$false) {
    $c=$Editor.Current;$focus=[System.Windows.Automation.AutomationElement]::FocusedElement;$f=$focus.Current;$r=$c.BoundingRectangle;$dc=$Dialog.Current
    $s=[ordered]@{editorProcessId=$c.ProcessId;focusProcessId=$f.ProcessId;editorRole=$c.ControlType.ProgrammaticName;focusRole=$f.ControlType.ProgrammaticName;editorEnabled=$c.IsEnabled;editorOffscreen=$c.IsOffscreen;editorWidth=$r.Width;editorHeight=$r.Height;focusSameEditor=$Editor.Equals($focus);editorInDialog=(Is-InSurface $Editor $Dialog);foregroundIsOwnedPicker=([CanvasPackageInput]::ForegroundMatches($PickerHandle) -and [CanvasPackageInput]::WindowPid($PickerHandle) -eq $process.Id);processLive=-not $process.HasExited;pickerProcessId=$dc.ProcessId;pickerRole=$dc.ControlType.ProgrammaticName;pickerHandleMatchesExpected=$PickerHandle -ne [IntPtr]::Zero -and [IntPtr]$dc.NativeWindowHandle -eq $PickerHandle;bindingAdmitted=$false;exactValue=$false;valueLength=$null;expectedLength=$null;valueSha256=$null;expectedSha256=$null;readRefusal=$null}
    if($Diagnostics){$s.identityWitness=Read-CapturedPickerIdentityWitness $Editor $focus $c $f $Dialog $PickerHandle $s.focusSameEditor}
    if(-not(Test-OwnedPickerValueBinding $s)){return $s}
    $s.bindingAdmitted=$true
    $value=$Editor.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    $witness=Read-BoundedPickerValueWitness $value $Expected $Diagnostics
    foreach($key in $witness.Keys){$s[$key]=$witness[$key]}
    return $s
}
function Test-OwnedPickerFocusSnapshot($Snapshot,[bool]$RequireTransition) {
    $owned=$Snapshot.focusProcessId -eq $process.Id -and $Snapshot.nativeWindowHandle -ne 0 -and $Snapshot.nativeProcessId -eq $process.Id -and $Snapshot.nativeIsPickerChild -and $Snapshot.kernelFocusHandle -ne 0 -and $Snapshot.kernelFocusProcessId -eq $process.Id -and $Snapshot.kernelFocusMatchesNative -and $Snapshot.focusInDialog -and $Snapshot.enabled -and -not $Snapshot.offscreen -and $Snapshot.width -gt 0 -and $Snapshot.height -gt 0 -and $Snapshot.pickerWindowHandle -ne 0 -and $Snapshot.pickerForeground -and $Snapshot.pickerKernelProcessId -eq $process.Id -and $Snapshot.processLive
    if(-not $owned){return $false}
    if($RequireTransition){return $Snapshot.focusRole -ceq 'ControlType.Edit' -and $Snapshot.nativeClassIsEdit -and $Snapshot.baselinePresent -and $Snapshot.identityChanged -and $Snapshot.nativeHandleChanged}
    return $true
}
function Read-OwnedPickerFocus($Dialog,[IntPtr]$PickerHandle,$Baseline=$null,[IntPtr]$BaselineHandle=[IntPtr]::Zero) {
    $focus=[System.Windows.Automation.AutomationElement]::FocusedElement;$c=$focus.Current;$r=$c.BoundingRectangle
    $native=[IntPtr]$c.NativeWindowHandle;$kernel=[CanvasPackageInput]::FocusHandle($PickerHandle,$process.Id)
    $s=[ordered]@{focusProcessId=$c.ProcessId;focusRole=$c.ControlType.ProgrammaticName;nativeWindowHandle=$native.ToInt64();nativeProcessId=$null;nativeIsPickerChild=$false;nativeClassIsEdit=$false;kernelFocusHandle=$kernel.ToInt64();kernelFocusProcessId=$null;kernelFocusMatchesNative=$native -ne [IntPtr]::Zero -and $native -eq $kernel;focusInDialog=$false;enabled=$c.IsEnabled;offscreen=$c.IsOffscreen;width=$r.Width;height=$r.Height;pickerWindowHandle=$PickerHandle.ToInt64();pickerForeground=[CanvasPackageInput]::ForegroundMatches($PickerHandle);pickerKernelProcessId=[CanvasPackageInput]::WindowPid($PickerHandle);processLive=-not $process.HasExited;baselinePresent=$null -ne $Baseline;identityChanged=$null;nativeHandleChanged=$null}
    if($s.focusProcessId -eq $process.Id){$s.focusInDialog=Is-InSurface $focus $Dialog}
    if($native -ne [IntPtr]::Zero){$s.nativeProcessId=[CanvasPackageInput]::WindowPid($native);if($s.nativeProcessId -eq $process.Id){$s.nativeIsPickerChild=[CanvasPackageInput]::IsChild($PickerHandle,$native);$s.nativeClassIsEdit=[CanvasPackageInput]::WindowClass($native) -ceq 'Edit'}}
    if($kernel -ne [IntPtr]::Zero){$s.kernelFocusProcessId=[CanvasPackageInput]::WindowPid($kernel)}
    if(-not(Test-OwnedPickerFocusSnapshot $s $false)){return}
    if($null -ne $Baseline){$s.identityChanged=-not $Baseline.Equals($focus);$s.nativeHandleChanged=$BaselineHandle -ne [IntPtr]::Zero -and $native -ne [IntPtr]::Zero -and $native -ne $BaselineHandle}
    if(Test-OwnedPickerFocusSnapshot $s ($null -ne $Baseline)){return [pscustomobject]@{element=$focus;snapshot=$s}}
}
function Choose-OwnFolder {
    Invoke-Button 'Set up Canvases'
    $dialog=Observe-OwnedPicker
    $handle=[IntPtr]$dialog.Current.NativeWindowHandle
    Check ([CanvasPackageInput]::SetForegroundWindow($handle) -and [CanvasPackageInput]::ForegroundMatches($handle)) 'Native folder address input targets exact owned picker HWND'
    try{
        # CtrlL is real queued input. Observe owned kernel/UIA focus BEFORE it,
        # then retain the actual new Edit after the shortcut changes focus.
        $beforeFocus=Wait-Observed {Read-OwnedPickerFocus $dialog $handle} 'Actual owned native picker focus before address shortcut'
        $result.pickerAddressFocusTransition=[ordered]@{baseline=$beforeFocus.snapshot;address=$null};Write-Result
        [CanvasPackageInput]::Chord(0x11,0x4C)
        $addressFocus=Wait-Observed {Read-OwnedPickerFocus $dialog $handle $beforeFocus.element ([IntPtr]$beforeFocus.snapshot.nativeWindowHandle)} 'Actual focused OS folder-address Edit'
        $address=$addressFocus.element;$result.pickerAddressFocusTransition.address=$addressFocus.snapshot;Write-Result
    }
    catch{
        $originalAddressFailure=$_
        try{Record-PickerControlWitness $dialog 'focused-address-timeout'}catch{$result.pickerFocusWitnessFailure=$_.Exception.GetType().FullName}
        throw $originalAddressFailure
    }
    Require-OwnedControl $address 'ControlType.Edit'
    Check (Is-InSurface $address $dialog) 'Actual focused folder-address editor belongs to exact native picker tree'
    [CanvasPackageInput]::TypeText($filesRoot)
    try{
        # SendInput queues real native input; observe the same exact owned field
        # and value with the existing bound before the original current-value Check.
        [void](Wait-Observed {$observation=Read-OwnedPickerValueObservation $address $dialog $handle $filesRoot;$observation.bindingAdmitted -and $observation.exactValue} 'Actual picker typed exact private empty fixture path')
        Check ($address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ceq $filesRoot) 'Actual picker typed exact private empty fixture path'
    }catch{
        $originalTypedValueFailure=$_
        try{$result.pickerTypedValueFailureObservation=Read-OwnedPickerValueObservation $address $dialog $handle $filesRoot $true;Write-Result}
        catch{$result.pickerTypedValueDiagnosticFailure=$_.Exception.GetType().FullName}
        throw $originalTypedValueFailure
    }
    [CanvasPackageInput]::Press(0x0D)
    [void](Wait-Observed {
        $condition=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)
        $witness=@($dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ToolBar -and -not $_.Current.IsOffscreen -and $_.Current.Name.EndsWith($filesRoot,[StringComparison]::OrdinalIgnoreCase)})
        try{$addressGone=$address.Current.IsOffscreen}catch{$addressGone=$true}
        $addressGone -and $witness.Count -eq 1
    } 'Actual native picker navigated breadcrumb to exact private fixture')
    Require-OwnedControl $dialog 'ControlType.Window'
    Check ([CanvasPackageInput]::ForegroundMatches($handle) -and [CanvasPackageInput]::WindowPid($handle) -eq $process.Id) 'Default-button read targets current exact owned native picker'
    # DM_GETDEFID/GetDlgItem observe the OS dialog's actual default control, with
    # a two-second abort-if-hung message bound; no caller-provided control ID.
    $defaultRead=[CanvasPackageInput]::ReadDefaultButton($handle)
    $result.pickerDefaultButtonRead=[ordered]@{messageCompleted=$defaultRead.MessageCompleted;win32Error=$defaultRead.Win32Error;hasDefaultMarker=$defaultRead.HasDefaultMarker;observedControlId=$defaultRead.ControlId;nativeWindowHandle=$defaultRead.WindowHandle.ToInt64();getDlgItemWin32Error=$defaultRead.GetDlgItemWin32Error;timeoutMilliseconds=2000}
    Check $defaultRead.MessageCompleted 'Actual bounded native default-button metadata read completed'
    $defaultHandle=$defaultRead.WindowHandle
    $defaults=@();$snapshot=[ordered]@{nativeWindowHandle=$defaultHandle.ToInt64();nativeProcessId=0;nativeIsPickerChild=$false;nativeClass='UNOBSERVED';nativeCaptionMatchesSelectFolder=$false;uiaProcessId=0;uiaWindowHandle=0;uiaRole='UNOBSERVED';uiaNameMatchesSelectFolder=$false}
    $result.pickerDefaultButtonObservation=$snapshot
    if($defaultHandle -ne [IntPtr]::Zero){
        $snapshot.nativeProcessId=[CanvasPackageInput]::WindowPid($defaultHandle);$snapshot.nativeIsPickerChild=[CanvasPackageInput]::IsChild($handle,$defaultHandle)
        if($snapshot.nativeProcessId -eq $process.Id -and $snapshot.nativeIsPickerChild){
            $snapshot.nativeClass=[CanvasPackageInput]::WindowClass($defaultHandle);$snapshot.nativeCaptionMatchesSelectFolder=[CanvasPackageInput]::WindowTitleEquals($defaultHandle,'Select Folder')
            $candidate=[System.Windows.Automation.AutomationElement]::FromHandle($defaultHandle);$c=$candidate.Current
            $snapshot.uiaProcessId=$c.ProcessId;$snapshot.uiaWindowHandle=[long]$c.NativeWindowHandle;$snapshot.uiaRole=$c.ControlType.ProgrammaticName;$snapshot.uiaNameMatchesSelectFolder=$c.Name -ceq 'Select Folder'
            if(Test-OwnedDefaultButtonSnapshot $snapshot){$defaults+=$candidate}
        }
    }
    Check ($defaults.Count -eq 1) 'Actual native folder picker exposes one observed standard default button'
    Require-OwnedControl $defaults[0] 'ControlType.Button'
    Check ([CanvasPackageInput]::ForegroundMatches($handle)) 'Folder choice remains on exact owned picker HWND'
    $defaults[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    [void](Wait-Observed {Test-Path -LiteralPath (Join-Path $filesRoot '.9to1-files/drive.json') -PathType Leaf} 'Actual Files owner created private workspace after native picker choice')
}
function Read-OwnedFilesConfigurationCompletion([bool]$Diagnostics=$false) {
    $observedState=Read-BoundedJson $homePath
    $currentProfiles=@($observedState.records | Where-Object {$_.recordId -ceq 'home.local-profile'})
    $currentConfigurations=@($observedState.records | Where-Object {$_.recordType -ceq 'files.native-workspace'})
    $witness=[ordered]@{profileCount=$currentProfiles.Count;configurationCount=$currentConfigurations.Count;currentProfileMatchesOriginal=$false;currentPrincipalMatchesOriginal=$false;configurationProfileMatchesOriginal=$false;exactRootMatchesOwnedFixture=$false}
    if($currentProfiles.Count -eq 1){$witness.currentProfileMatchesOriginal=[Guid]$currentProfiles[0].payload.ProfileId -eq $privateProfileId;$witness.currentPrincipalMatchesOriginal=$currentProfiles[0].payload.PrincipalDigest -ceq $principalDigest}
    if($currentConfigurations.Count -eq 1){$witness.configurationProfileMatchesOriginal=[Guid]$currentConfigurations[0].payload.ProfileId -eq $privateProfileId;$witness.exactRootMatchesOwnedFixture=$currentConfigurations[0].payload.RootDirectory -ceq $filesRoot}
    if($Diagnostics){return $witness}
    if($witness.profileCount -eq 1 -and $witness.configurationCount -eq 1 -and $witness.currentProfileMatchesOriginal -and $witness.currentPrincipalMatchesOriginal -and $witness.configurationProfileMatchesOriginal -and $witness.exactRootMatchesOwnedFixture){return [pscustomobject]@{homeState=$observedState}}
}
function Read-CurrentCanvas {
    $observedHomeState=Read-BoundedJson $homePath
    $profiles=@($observedHomeState.records | Where-Object {$_.recordId -ceq 'home.local-profile'})
    $configs=@($observedHomeState.records | Where-Object {$_.recordType -ceq 'files.native-workspace'})
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
    Initialize-StandardUiAutomationProviders
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
    public static IntPtr ForegroundHandle() { return GetForegroundWindow(); }
    public static int WindowPid(IntPtr window) { uint pid; GetWindowThreadProcessId(window,out pid); return (int)pid; }
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    public static IntPtr OwnerHandle(IntPtr window) { return GetWindow(window,4); }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,System.Text.StringBuilder text,int maximum);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,System.Text.StringBuilder text,int maximum);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    public static bool WindowExists(IntPtr window) { return IsWindow(window); }
    public static bool WindowTitleEquals(IntPtr window,string expected) { var text=new System.Text.StringBuilder(512); GetWindowText(window,text,text.Capacity); return string.Equals(text.ToString(),expected,StringComparison.Ordinal); }
    public static string WindowClass(IntPtr window) { var text=new System.Text.StringBuilder(256); GetClassName(window,text,text.Capacity); return text.ToString(); }
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr window,uint message,UIntPtr wParam,IntPtr lParam,uint flags,uint milliseconds,out UIntPtr result);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr GetDlgItem(IntPtr window,int controlId);
    [DllImport("kernel32.dll")] static extern void SetLastError(uint error);
    public sealed class DefaultButtonRead { public bool MessageCompleted; public int Win32Error; public bool HasDefaultMarker; public int ControlId; public IntPtr WindowHandle; public int GetDlgItemWin32Error; }
    public static DefaultButtonRead ReadDefaultButton(IntPtr window) { var read=new DefaultButtonRead(); UIntPtr result; SetLastError(0); read.MessageCompleted=SendMessageTimeout(window,0x0400,UIntPtr.Zero,IntPtr.Zero,3,2000,out result)!=IntPtr.Zero; if(!read.MessageCompleted) {read.Win32Error=Marshal.GetLastWin32Error();return read;} var value=result.ToUInt64(); read.HasDefaultMarker=((value>>16)&0xffff)==0x534b; if(!read.HasDefaultMarker) return read; read.ControlId=(int)(value&0xffff); if(read.ControlId==0) return read; SetLastError(0); read.WindowHandle=GetDlgItem(window,read.ControlId); if(read.WindowHandle==IntPtr.Zero) read.GetDlgItemWin32Error=Marshal.GetLastWin32Error(); return read; }
    [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent,IntPtr child);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left,top,right,bottom; }
    [StructLayout(LayoutKind.Sequential)] struct GUIINFO { public uint size,flags; public IntPtr active,focus,capture,menuOwner,moveSize,caret; public RECT caretRect; }
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread,ref GUIINFO info);
    public static IntPtr FocusHandle(IntPtr window,int expected) { uint pid; var thread=GetWindowThreadProcessId(window,out pid); if(pid!=(uint)expected) return IntPtr.Zero; var info=new GUIINFO {size=(uint)Marshal.SizeOf(typeof(GUIINFO))}; return GetGUIThreadInfo(thread,ref info) ? info.focus : IntPtr.Zero; }
    delegate bool ENUMCHILD(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,ENUMCHILD callback,IntPtr state);
    public static IntPtr[] ChildHandles(IntPtr parent) { var children=new System.Collections.Generic.List<IntPtr>(); ENUMCHILD callback=(window,state)=> {children.Add(window);return children.Count<128;}; EnumChildWindows(parent,callback,IntPtr.Zero); GC.KeepAlive(callback); return children.ToArray(); }
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
    $observedHomeState=Read-BoundedJson $homePath;$profiles=@($observedHomeState.records | Where-Object {$_.recordId -ceq 'home.local-profile'})
    Check ($profiles.Count -eq 1 -and [Guid]$profiles[0].payload.ProfileId -ne [Guid]::Empty) 'Actual OS-backed canonical Home profile exists after native initialization'
    $privateProfileId=[Guid]$profiles[0].payload.ProfileId
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent();$sha=[Security.Cryptography.SHA256]::Create()
    try{$principalBytes=[Text.Encoding]::UTF8.GetBytes('windows-sid:'+$identity.User.Value);$principalDigest=([BitConverter]::ToString($sha.ComputeHash($principalBytes))).Replace('-','')}
    finally{$identity.Dispose();$sha.Dispose()}
    Check ($profiles[0].payload.PrincipalDigest -ceq $principalDigest) 'Actual private Home profile binds the original OS process principal'
    $profileBytes=[Text.Encoding]::UTF8.GetBytes([string]$profiles[0].payload.ProfileId);$sha=[Security.Cryptography.SHA256]::Create()
    try{$result.privateProfileIdentitySha256=([BitConverter]::ToString($sha.ComputeHash($profileBytes))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
    try{
        # drive.json exists before canonical setup publishes its final guarded
        # Home record. Observe that completion without further native input.
        $completedConfiguration=Wait-Observed {Read-OwnedFilesConfigurationCompletion} 'Actual original-profile Files configuration completion'
        $observedHomeState=$completedConfiguration.homeState
    }catch{
        $originalConfigurationCompletionFailure=$_
        try{$result.filesConfigurationCompletionFailureObservation=Read-OwnedFilesConfigurationCompletion $true;Write-Result}
        catch{$result.filesConfigurationCompletionDiagnosticFailureType=$_.Exception.GetType().FullName}
        throw $originalConfigurationCompletionFailure
    }
    $configs=@($observedHomeState.records | Where-Object {$_.recordType -ceq 'files.native-workspace'})
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
            $result.failureCleanup=[ordered]@{processId=$process.Id;closeRequested=$false;exited=$false;exitCode=$null}
            if(-not $process.HasExited -and $result.status -ceq 'FAILED_OR_BLOCKED_UNACCEPTED'){
                try{Cancel-OwnPickerOnFailure}catch{$result.pickerCancellationFailure=$_.Exception.GetType().FullName}
            }
            if(-not $process.HasExited){$result.failureCleanup.closeRequested=$process.CloseMainWindow();if(-not $process.WaitForExit(5000)){$process.Kill();$result.forcedCleanup=$true;[void]$process.WaitForExit(5000)}}
            if($process.HasExited){$result.failureCleanup.exited=$true;$result.failureCleanup.exitCode=$process.ExitCode;if(-not (Drain-OwnedLogs)){$result.cleanupDrainFailure='Own streams did not complete bounded drain.'}}
        }catch{$result.cleanupFailure=$_.Exception.GetType().FullName}finally{$process.Dispose()}
    }
    if($result.forcedCleanup -or $result.Contains('cleanupDrainFailure') -or $result.Contains('cleanupFailure') -or $result.Contains('pickerCancellationFailure')){$result.status='FAILED_OR_BLOCKED_UNACCEPTED';$exitCode=1}
    Write-Result
}
exit $exitCode
