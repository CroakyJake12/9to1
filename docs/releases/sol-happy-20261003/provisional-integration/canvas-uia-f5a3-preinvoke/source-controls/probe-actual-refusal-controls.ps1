$ErrorActionPreference='Stop'
$source='/workspace/team-c-c3-canvas-uia-providers/.github/scripts/canvas-packaged-ui-controls.ps1'
$tokens=$null;$errors=$null;$ast=[System.Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Actual source parser errors.'}
foreach($fn in @('Check','Require-OwnedControl','Test-OwnedPickerSnapshot','Observe-OwnedPicker','Cancel-OwnPickerOnFailure')){
    $definition=$ast.FindAll({param($node)$node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $fn},$true)[0]
    Invoke-Expression $definition.Extent.Text
}
function Write-Result {$script:writeCount++}
$process=[pscustomobject]@{Id=77};$writeCount=0;$controls=@()
foreach($case in @(
    @{name='foreign-process';pid=78;role='ControlType.Edit';enabled=$true;offscreen=$false;width=1;height=1},
    @{name='wrong-role';pid=77;role='ControlType.Button';enabled=$true;offscreen=$false;width=1;height=1},
    @{name='disabled';pid=77;role='ControlType.Edit';enabled=$false;offscreen=$false;width=1;height=1},
    @{name='offscreen';pid=77;role='ControlType.Edit';enabled=$true;offscreen=$true;width=1;height=1},
    @{name='zero-width';pid=77;role='ControlType.Edit';enabled=$true;offscreen=$false;width=0;height=1},
    @{name='zero-height';pid=77;role='ControlType.Edit';enabled=$true;offscreen=$false;width=1;height=0})){
    $result=[ordered]@{checks=@()}
    $element=[pscustomobject]@{Current=[pscustomobject]@{ProcessId=$case.pid;ControlType=[pscustomobject]@{ProgrammaticName=$case.role};IsEnabled=$case.enabled;IsOffscreen=$case.offscreen;BoundingRectangle=[pscustomobject]@{Width=$case.width;Height=$case.height}}}
    $caught=$null;try{Require-OwnedControl $element 'ControlType.Edit'}catch{$caught=$_}
    if($null -eq $caught -or $result.checks[-1].passed){throw 'Actual owning-control guard failed to refuse.'}
    if($caught.Exception.Message -notin @('Native control has the exact owned process and role','Native control is enabled and visible with real geometry')){throw 'Refusal passed the local capability guard.'}
    $controls+=[ordered]@{case=$case.name;passed=$true;actualSourceGuardMessage=$caught.Exception.Message}
}
$text=Get-Content -Raw $source
$native=$text.Substring($text.IndexOf("using System;`nusing System.ComponentModel;"));$native=$native.Substring(0,$native.IndexOf("`n'@"))
Add-Type -TypeDefinition $native
foreach($case in @(@{name='left-outside';x=-1;y=0;w=1024;h=768},@{name='right-outside';x=1024;y=0;w=1024;h=768},@{name='top-outside';x=0;y=-1;w=1024;h=768},@{name='bottom-outside';x=0;y=768;w=1024;h=768},@{name='invalid-width';x=0;y=0;w=1;h=768},@{name='invalid-height';x=0;y=0;w=1024;h=1})){
    $caught=$null;try{[CanvasPackageInput]::Move($case.x,$case.y,0,0,$case.w,$case.h)}catch{$caught=$_}
    if($null -eq $caught -or $caught.Exception.InnerException.GetType().FullName -cne 'System.ArgumentOutOfRangeException' -or $caught.Exception.Message -notlike '*Native pointer must fit actual virtual screen bounds.*'){throw 'Actual C# mouse-bound refusal did not stop before external OS input.'}
    $controls+=[ordered]@{case=$case.name;passed=$true;actualSourceGuardMessage='Native pointer must fit actual virtual screen bounds.'}
}
$finally=$ast.FindAll({param($node)$node -is [System.Management.Automation.Language.TryStatementAst] -and $null -ne $node.Finally -and $node.Finally.Extent.Text.Contains("mouseReleaseFailure")},$true)[0].Finally
foreach($case in @('unreleased-mouse','forced-cleanup','drain-failure','cleanup-failure','cancel-failure','clean')){
    $process=$null;$mouseDown=$case -ceq 'unreleased-mouse';$exitCode=0
    $result=[ordered]@{status='CONTROLLED_PASS';forcedCleanup=$case -ceq 'forced-cleanup'}
    if($case -ceq 'drain-failure'){$result.cleanupDrainFailure='CONTROLLED'}
    if($case -ceq 'cleanup-failure'){$result.cleanupFailure='CONTROLLED'}
    if($case -ceq 'cancel-failure'){$result.pickerCancellationFailure='CONTROLLED'}
    . ([scriptblock]::Create($finally.Extent.Text.Trim().Substring(1,$finally.Extent.Text.Trim().Length-2)))
    if($case -ceq 'clean'){if($result.status -cne 'CONTROLLED_PASS' -or $exitCode -ne 0){throw 'Clean finally changed controlled receipt.'}}
    elseif($result.status -cne 'FAILED_OR_BLOCKED_UNACCEPTED' -or $exitCode -ne 1){throw 'Actual finally did not block success after controlled input/cleanup failure.'}
    $controls+=[ordered]@{case=$case;passed=$true;actualSourceGuardMessage=$result.status}
}
$main=$ast.FindAll({param($node)$node -is [System.Management.Automation.Language.TryStatementAst] -and $null -ne $node.Finally -and $node.Finally.Extent.Text.Contains("mouseReleaseFailure")},$true)[0]
$process=$null;$result=[ordered]@{status='CONTROLLED';stage='CONTROLLED_PRIVATE_READ_STAGE'}
try{throw 'CONTROLLED_PRIVATE_EXCEPTION_SENTINEL'}catch{. ([scriptblock]::Create($main.CatchClauses[0].Body.Extent.Text.Trim().Substring(1,$main.CatchClauses[0].Body.Extent.Text.Trim().Length-2)))}
if(($result|ConvertTo-Json -Depth 8) -like '*CONTROLLED_PRIVATE_EXCEPTION_SENTINEL*' -or $result.failure.rawExceptionText -cne 'WITHHELD' -or $result.failure.stage -cne 'CONTROLLED_PRIVATE_READ_STAGE' -or $result.status -cne 'FAILED_OR_BLOCKED_UNACCEPTED'){throw 'Actual outer catch retained uncontrolled private exception text.'}
$controls+=[ordered]@{case='private-external-exception-redacted';passed=$true;actualSourceGuardMessage='WITHHELD; controlled stage retained'}
$process=[pscustomobject]@{Id=77}
$baseline=@{foregroundProcessId=77;nativeTitleMatches=$true;ownerChainMatches=$true;uiaProcessId=77;uiaRole='ControlType.Window';uiaTitleMatches=$true;uiaWindowHandle=99;foregroundWindowHandle=99}
if(-not (Test-OwnedPickerSnapshot $baseline)){throw 'Owned picker metadata positive predicate failed.'}
foreach($case in @(@{field='foregroundProcessId';value=78},@{field='nativeTitleMatches';value=$false},@{field='ownerChainMatches';value=$false},@{field='uiaProcessId';value=78},@{field='uiaRole';value='ControlType.Edit'},@{field='uiaTitleMatches';value=$false},@{field='uiaWindowHandle';value=100},@{field='foregroundWindowHandle';value=0})){
    $snapshot=$baseline.Clone();$snapshot[$case.field]=$case.value
    if(Test-OwnedPickerSnapshot $snapshot){throw 'Actual owned picker predicate admitted foreign/missing kernel/UIA metadata.'}
    $controls+=[ordered]@{case='picker-refuses-'+$case.field;passed=$true;actualSourceGuardMessage='Actual snapshot predicate refused before any capability/action'}
}
function Wait-Observed {throw 'CONTROLLED_ORIGINAL_PICKER_TIMEOUT'}
foreach($case in @('capability-refusal','output-refusal')){
    if($case -ceq 'capability-refusal'){function Record-OwnedPickerTreeWitness {throw [System.InvalidOperationException]::new('CONTROLLED_PRIVATE_CAPABILITY_EXCEPTION')}}
    else{function Record-OwnedPickerTreeWitness {throw [System.IO.IOException]::new('CONTROLLED_PRIVATE_OUTPUT_EXCEPTION')}}
    $result=[ordered]@{checks=@()};$caught=$null;try{Observe-OwnedPicker}catch{$caught=$_}
    if($null -eq $caught -or $caught.Exception.Message -cne 'CONTROLLED_ORIGINAL_PICKER_TIMEOUT' -or ($result|ConvertTo-Json) -like '*CONTROLLED_PRIVATE_*'){throw 'Actual diagnostic catch replaced original timeout or retained secondary private text.'}
    $controls+=[ordered]@{case='picker-diagnostic-'+$case;passed=$true;actualSourceGuardMessage='Original timeout retained; secondary type only'}
}
function Find-OwnedForegroundPicker {return $null}
$result=[ordered]@{checks=@()};Cancel-OwnPickerOnFailure
if($result.Contains('pickerCancellation')){throw 'Absent proven picker recorded a cancellation.'}
$controls+=[ordered]@{case='absent-picker-never-cancelled';passed=$true;actualSourceGuardMessage='Actual Cancel function returned before any native action'}
Add-Type -TypeDefinition @'
public sealed class ControlledOwnedProcess {
 public int Id {get{return 77;}}
 public int ExitCode {get{return 0;}}
 public bool HasExited {get;private set;}
 public int CloseCalls {get;private set;}
 public int DisposeCalls {get;private set;}
 public bool CloseMainWindow() {CloseCalls++;return true;}
 public bool WaitForExit(int timeout) {HasExited=true;return true;}
 public void Kill() {throw new System.InvalidOperationException("Unexpected force cleanup");}
 public void Dispose() {DisposeCalls++;}
}
'@
function Cancel-OwnPickerOnFailure {throw [System.InvalidOperationException]::new('CONTROLLED_PRIVATE_CANCELLATION_FAILURE')}
function Drain-OwnedLogs {return $true}
$process=[ControlledOwnedProcess]::new();$mouseDown=$false;$exitCode=0;$result=[ordered]@{status='FAILED_OR_BLOCKED_UNACCEPTED';forcedCleanup=$false}
. ([scriptblock]::Create($finally.Extent.Text.Trim().Substring(1,$finally.Extent.Text.Trim().Length-2)))
if($process.CloseCalls -ne 1 -or $process.DisposeCalls -ne 1 -or $result.status -cne 'FAILED_OR_BLOCKED_UNACCEPTED' -or $exitCode -ne 1 -or ($result|ConvertTo-Json) -like '*CONTROLLED_PRIVATE_CANCELLATION_FAILURE*' -or -not $result.Contains('pickerCancellationFailure') -or -not $result.failureCleanup.closeRequested -or -not $result.failureCleanup.exited -or $result.failureCleanup.exitCode -ne 0){throw 'Actual cancellation refusal prevented owned cleanup or retained private text/admitted success.'}
$controls+=[ordered]@{case='cancellation-refusal-still-closes-disposes-and-fails';passed=$true;actualSourceGuardMessage='Actual outer finally retained refusal and continued owned cleanup'}
$fn=$ast.FindAll({param($node)$node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Find-OwnedForegroundPicker'},$true)[0]
$routing=$fn.FindAll({param($node)$node -is [System.Management.Automation.Language.IfStatementAst] -and $node.Extent.Text.StartsWith('if($Cleanup)')},$true)
if($routing.Count -ne 1){throw 'Actual observation routing statement was not unique.'}
$result=[ordered]@{};$snapshot=[ordered]@{phase='CONTROLLED_ORIGINAL_PICKER_WITNESS'};$Cleanup=$false
. ([scriptblock]::Create($routing[0].Extent.Text))
$originalSnapshot=$result.pickerObservation;$snapshot=[ordered]@{phase='CONTROLLED_CLEANUP_WITNESS'};$Cleanup=$true
. ([scriptblock]::Create($routing[0].Extent.Text))
if($result.pickerObservation.phase -cne 'CONTROLLED_ORIGINAL_PICKER_WITNESS' -or $result.pickerCleanupObservation.phase -cne 'CONTROLLED_CLEANUP_WITNESS' -or -not [object]::ReferenceEquals($result.pickerObservation,$originalSnapshot)){throw 'Actual cleanup observation overwrote original phase witness.'}
$controls+=[ordered]@{case='cleanup-observation-preserves-original-phase-witness';passed=$true;actualSourceGuardMessage='Actual routing statement retained separate original and cleanup snapshots'}
$choose=$ast.FindAll({param($n)$n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Choose-OwnFolder'},$true)[0]
$addressTry=$choose.FindAll({param($n)$n -is [System.Management.Automation.Language.TryStatementAst] -and $n.Extent.Text.StartsWith('try{$address=Wait-Observed')},$true)[0]
$cancel=$ast.FindAll({param($n)$n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Cancel-OwnPickerOnFailure'},$true)[0]
$cancelTry=$cancel.FindAll({param($n)$n -is [System.Management.Automation.Language.TryStatementAst] -and $n.Extent.Text.StartsWith('try{$cancel=Find-Unique')},$true)[0]
foreach($boundary in @(@{name='address';block=$addressTry},@{name='cancel';block=$cancelTry})){
 foreach($mode in @('capability-refusal','output-refusal')){
  if($mode -ceq 'capability-refusal'){function Record-PickerControlWitness {throw [System.InvalidOperationException]::new('CONTROLLED_PRIVATE_DIAGNOSTIC_CAPABILITY')}}
  else{function Record-PickerControlWitness {throw [System.IO.IOException]::new('CONTROLLED_PRIVATE_DIAGNOSTIC_WRITE')}}
  $result=[ordered]@{};$dialog=$null;$caught=$null
  try{throw ('CONTROLLED_ORIGINAL_'+$boundary.name+'_FAILURE')}catch{try{. ([scriptblock]::Create($boundary.block.CatchClauses[0].Body.Extent.Text.Trim().Substring(1,$boundary.block.CatchClauses[0].Body.Extent.Text.Trim().Length-2)))}catch{$caught=$_}}
  if($null -eq $caught -or $caught.Exception.Message -cne ('CONTROLLED_ORIGINAL_'+$boundary.name+'_FAILURE') -or ($result|ConvertTo-Json) -like '*CONTROLLED_PRIVATE_*'){throw 'Actual diagnostic boundary replaced original failure or retained private secondary text.'}
  $controls+=[ordered]@{case=$boundary.name+'-diagnostic-'+$mode;passed=$true;actualSourceGuardMessage='Actual catch retained original failure; secondary type only'}
 }
}
foreach($name in @('Test-GenuineUiAutomationAssembly','Test-OwnedDefaultButtonSnapshot','Read-PickerPeerWitness')) {
 $fn=$ast.FindAll({param($n)$n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true)[0]
 Invoke-Expression $fn.Extent.Text
}
$baseline=@{name='UIAutomationClientsideProviders';version='4.0.0.0';culture='';publicKeyToken='31bf3856ad364e35';globalAssemblyCache=$true;beneathWindowsDirectory=$true;location='C:\Windows\assembly\controlled-provider.dll';sha256=('a'*64)}
if(-not(Test-GenuineUiAutomationAssembly $baseline 'UIAutomationClientsideProviders' '4.0.0.0' '')){throw 'Actual provider guard refused controlled valid tuple.'}
$controls+=[ordered]@{case='standard-provider-valid-tuple';passed=$true;actualSourceGuardMessage='Actual pure predicate admitted controlled genuine installed tuple only; no registration performed'}
foreach($case in @(@{field='name';value='ControlledForeignAssembly'},@{field='version';value='5.0.0.0'},@{field='culture';value='controlled'},@{field='publicKeyToken';value='0000000000000000'},@{field='globalAssemblyCache';value=$false},@{field='beneathWindowsDirectory';value=$false},@{field='location';value=''},@{field='sha256';value='invalid'})) {
 $snapshot=$baseline.Clone();$snapshot[$case.field]=$case.value
 if(Test-GenuineUiAutomationAssembly $snapshot 'UIAutomationClientsideProviders' '4.0.0.0' ''){throw 'Actual assembly guard admitted mismatched installed provider tuple.'}
 $controls+=[ordered]@{case='standard-provider-refuses-'+$case.field;passed=$true;actualSourceGuardMessage='Actual provider predicate refused before registration'}
}
$process=[pscustomobject]@{Id=77}
$baseline=@{nativeWindowHandle=19;nativeProcessId=77;nativeIsPickerChild=$true;nativeClass='Button';nativeCaptionMatchesSelectFolder=$true;uiaProcessId=77;uiaWindowHandle=19;uiaRole='ControlType.Button';uiaNameMatchesSelectFolder=$true}
if(-not(Test-OwnedDefaultButtonSnapshot $baseline)){throw 'Actual default-button predicate refused controlled valid owned native/UIA tuple.'}
$controls+=[ordered]@{case='owned-default-button-valid-tuple';passed=$true;actualSourceGuardMessage='Actual pure predicate admitted controlled owned tuple only; no OS metadata/read/input performed'}
foreach($case in @(@{field='nativeWindowHandle';value=0},@{field='nativeProcessId';value=78},@{field='nativeIsPickerChild';value=$false},@{field='nativeClass';value='Edit'},@{field='nativeCaptionMatchesSelectFolder';value=$false},@{field='uiaProcessId';value=78},@{field='uiaWindowHandle';value=20},@{field='uiaRole';value='ControlType.Pane'},@{field='uiaNameMatchesSelectFolder';value=$false})) {
 $snapshot=$baseline.Clone();$snapshot[$case.field]=$case.value
 if(Test-OwnedDefaultButtonSnapshot $snapshot){throw 'Actual default-button guard admitted foreign, Pane or unobserved control.'}
 $controls+=[ordered]@{case='default-button-refuses-'+$case.field;passed=$true;actualSourceGuardMessage='Actual metadata predicate refused before any native/default button action'}
}
# Controlled pattern capability types exercise only the actual diagnostic reader
# in this Linux process. They are never application/provider/UI acceptance data.
Add-Type -TypeDefinition @'
namespace System.Windows.Automation {
 public static class ValuePattern {public static readonly object Pattern=new object();}
 public static class InvokePattern {public static readonly object Pattern=new object();}
}
'@
$peer=[pscustomobject]@{Current=[pscustomobject]@{ProcessId=77;NativeWindowHandle=0;BoundingRectangle=[pscustomobject]@{X=0;Y=0;Width=1;Height=1};ControlType=[pscustomobject]@{ProgrammaticName='ControlType.Pane'};IsEnabled=$true;IsOffscreen=$false;Name='CONTROLLED_PRIVATE_PEER_NAME'}}
$peer|Add-Member ScriptMethod TryGetCurrentPattern {param($pattern,$value) return $false}
$w=Read-PickerPeerWitness $peer ([IntPtr]::Zero)
if($w.readPhase -cne 'completed' -or $w.legacyTypeAvailable -or $w.legacyReadStatus -cne 'MANAGED_TYPE_UNAVAILABLE' -or $null -ne $w.legacyPattern -or $null -ne $w.legacyDefaultButton -or $null -ne $w.readFailure -or ($w|ConvertTo-Json) -like '*CONTROLLED_PRIVATE_PEER_NAME*'){throw 'Actual diagnostic reader misattributed absent managed Legacy type or leaked private peer name.'}
$controls+=[ordered]@{case='missing-managed-legacy-type-is-unobserved';passed=$true;actualSourceGuardMessage='Actual reader retained common observations, explicit unavailable type and null Legacy/default fields; no name text'}
$peer|Add-Member ScriptMethod TryGetCurrentPattern {param($pattern,$value) throw 'CONTROLLED_PRIVATE_PATTERN_FAILURE'} -Force
$w=Read-PickerPeerWitness $peer ([IntPtr]::Zero)
if($w.readPhase -cne 'value-pattern' -or $null -eq $w.readFailure -or $w.legacyReadStatus -cne 'NOT_READ' -or $null -ne $w.legacyPattern -or ($w|ConvertTo-Json) -like '*CONTROLLED_PRIVATE_PATTERN_FAILURE*'){throw 'Actual diagnostic reader lost failed read phase or retained private capability exception text.'}
$controls+=[ordered]@{case='pattern-read-refusal-retains-phase-not-text';passed=$true;actualSourceGuardMessage='Actual reader records failed value read phase/type only; Legacy/default remain unobserved'}
[ordered]@{status='PASS';parserVersion=$PSVersionTable.PSVersion.ToString();parserErrors=$errors.Count;sourceSha256=(Get-FileHash $source -Algorithm SHA256).Hash.ToLower();controlsPassed=$controls.Count;controls=$controls;nativeCSharpCompilation='PASS';qualification='Actual source functions/C# input-bounds/finally refusal controls only; controlled process/element metadata and failure outcomes. Private exception sentinel withheld by actual outer catch. No external Windows API invocation, live app/UIA/provider/profile/model or actual native acceptance.'}|ConvertTo-Json -Depth 8|Set-Content -Encoding utf8NoBOM '/workspace/team-c-resume-evidence/c3-canvas-uia-providers-20261004/actual-refusal-controls-receipt.json'
Write-Output ('Actual Canvas provider/default-control/diagnostic + preserved input/cleanup refusal controls: '+$controls.Count+'/'+$controls.Count+' PASS; actual C# compile PASS; parser0errors.')
