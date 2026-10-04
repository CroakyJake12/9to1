$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$evidence='/workspace/team-c-resume-evidence/c3-canvas-address-focus-transition-20261004'
$source='/workspace/team-c-c3-canvas-address-focus-transition/.github/scripts/canvas-packaged-ui-controls.ps1'
$t=$null;$errors=$null;$ast=[System.Management.Automation.Language.Parser]::ParseFile($source,[ref]$t,[ref]$errors)
if($errors.Count){throw 'Actual source parser errors.'}
foreach($name in @('Test-OwnedPickerFocusSnapshot','Read-OwnedPickerFocus')){
    $f=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true));if($f.Count -ne 1){throw 'Actual new focus helper not unique.'};Invoke-Expression $f[0].Extent.Text
}
$controls=@();$process=[pscustomobject]@{Id=77;HasExited=$false}
function Pass([string]$Name){$script:controls+=[ordered]@{case=$Name;passed=$true}}
$valid=@{focusProcessId=77;focusRole='ControlType.Edit';nativeWindowHandle=22;nativeProcessId=77;nativeIsPickerChild=$true;nativeClassIsEdit=$true;kernelFocusHandle=22;kernelFocusProcessId=77;kernelFocusMatchesNative=$true;focusInDialog=$true;enabled=$true;offscreen=$false;width=1;height=1;pickerWindowHandle=19;pickerForeground=$true;pickerKernelProcessId=77;processLive=$true;baselinePresent=$true;identityChanged=$true;nativeHandleChanged=$true}
if(-not(Test-OwnedPickerFocusSnapshot $valid $true)){throw 'Controlled complete changed-address focus tuple refused.'};Pass 'owned-changed-edit-complete-tuple'
$b=$valid.Clone();$b.focusRole='ControlType.Button';$b.nativeClassIsEdit=$false;$b.baselinePresent=$false;$b.identityChanged=$null;$b.nativeHandleChanged=$null
if(-not(Test-OwnedPickerFocusSnapshot $b $false) -or (Test-OwnedPickerFocusSnapshot $b $true)){throw 'Owned baseline was confused with changed address Edit.'};Pass 'owned-baseline-any-genuine-role-is-not-address-acceptance'
foreach($case in @(
    @{field='focusProcessId';value=78},@{field='nativeWindowHandle';value=0},@{field='nativeProcessId';value=78},@{field='nativeIsPickerChild';value=$false},
    @{field='kernelFocusHandle';value=0},@{field='kernelFocusProcessId';value=78},@{field='kernelFocusMatchesNative';value=$false},@{field='focusInDialog';value=$false},
    @{field='enabled';value=$false},@{field='offscreen';value=$true},@{field='width';value=0},@{field='height';value=0},
    @{field='pickerWindowHandle';value=0},@{field='pickerForeground';value=$false},@{field='pickerKernelProcessId';value=78},@{field='processLive';value=$false},
    @{field='focusRole';value='ControlType.Pane'},@{field='nativeClassIsEdit';value=$false},@{field='baselinePresent';value=$false},@{field='identityChanged';value=$false},@{field='nativeHandleChanged';value=$false})){
    $s=$valid.Clone();$s[$case.field]=$case.value
    if(Test-OwnedPickerFocusSnapshot $s $true){throw 'Actual address transition predicate admitted a missing ownership/transition proof.'};Pass ('address-transition-refuses-'+$case.field)
}
# Controlled signatures have no OS/native provider implementation and are used
# only to exercise actual new source guards/order in this fresh Linux process.
Add-Type -TypeDefinition @'
using System;
namespace System.Windows.Automation {
    public class AutomationElement {
        public object Current;public int ControlledIdentity;
        public static AutomationElement FocusedElement;
        public static int EqualityCalls;
        public override bool Equals(object other){EqualityCalls++;AutomationElement el=other as AutomationElement;return el!=null && el.ControlledIdentity==ControlledIdentity;}
        public override int GetHashCode(){return ControlledIdentity;}
    }
}
public static class CanvasPackageInput {
    public static bool Foreground=true;public static bool Child=true;public static bool ClassEdit=true;public static int PickerPid=77;public static IntPtr KernelFocus=(IntPtr)21;
    public static int ChordCalls;public static ushort FirstKey;public static ushort SecondKey;
    public static System.Windows.Automation.AutomationElement NextFocus;public static IntPtr NextKernelFocus;
    public static IntPtr FocusHandle(IntPtr p,int pid){return KernelFocus;}
    public static int WindowPid(IntPtr h){return h==(IntPtr)19 ? PickerPid : h==(IntPtr)99 ? 78 : h==IntPtr.Zero ? 0 : 77;}
    public static bool IsChild(IntPtr p,IntPtr h){return Child;}
    public static string WindowClass(IntPtr h){return ClassEdit ? "Edit" : "CONTROLLED_PRIVATE_CLASS";}
    public static bool ForegroundMatches(IntPtr h){return Foreground;}
    public static void Chord(ushort a,ushort b){ChordCalls++;FirstKey=a;SecondKey=b;System.Windows.Automation.AutomationElement.FocusedElement=NextFocus;KernelFocus=NextKernelFocus;}
}
'@
$script:inDialog=$true;$script:treeCalls=0
function Is-InSurface($Element,$Surface){$script:treeCalls++;return $script:inDialog}
function Reset-Focus {
    $script:baseline=[System.Windows.Automation.AutomationElement]::new();$script:changed=[System.Windows.Automation.AutomationElement]::new()
    foreach($e in @($baseline,$changed)){$e.Current=[pscustomobject]@{ProcessId=77;ControlType=[pscustomobject]@{ProgrammaticName='ControlType.Edit'};NativeWindowHandle=21;IsEnabled=$true;IsOffscreen=$false;BoundingRectangle=[pscustomobject]@{Width=1;Height=1};Name='CONTROLLED_PRIVATE_NAME'}}
    $baseline.ControlledIdentity=1;$changed.ControlledIdentity=2;$changed.Current.NativeWindowHandle=22
    $script:dialog=[pscustomobject]@{};[System.Windows.Automation.AutomationElement]::FocusedElement=$baseline
    [System.Windows.Automation.AutomationElement]::EqualityCalls=0
    [CanvasPackageInput]::Foreground=$true;[CanvasPackageInput]::Child=$true;[CanvasPackageInput]::ClassEdit=$true;[CanvasPackageInput]::PickerPid=77;[CanvasPackageInput]::KernelFocus=[IntPtr]21
    [CanvasPackageInput]::ChordCalls=0;[CanvasPackageInput]::NextFocus=$changed;[CanvasPackageInput]::NextKernelFocus=[IntPtr]22
    $process.HasExited=$false;$script:inDialog=$true;$script:treeCalls=0
}
Reset-Focus
$b=Read-OwnedPickerFocus $dialog ([IntPtr]19)
if($null -eq $b -or -not [object]::ReferenceEquals($b.element,$baseline) -or $b.snapshot.baselinePresent -or ($b.snapshot|ConvertTo-Json) -like '*CONTROLLED_PRIVATE_*'){throw 'Actual helper did not retain genuine controlled baseline without private name.'};Pass 'actual-helper-owned-baseline-snapshot-only'
$same=Read-OwnedPickerFocus $dialog ([IntPtr]19) $baseline ([IntPtr]21)
if($null -ne $same){throw 'Actual helper retained pre-shortcut focus as changed address.'};Pass 'same-focused-editor-refused-as-transition'
[System.Windows.Automation.AutomationElement]::FocusedElement=$changed;[CanvasPackageInput]::KernelFocus=[IntPtr]22
$a=Read-OwnedPickerFocus $dialog ([IntPtr]19) $baseline ([IntPtr]21)
if($null -eq $a -or -not [object]::ReferenceEquals($a.element,$changed) -or -not $a.snapshot.identityChanged -or -not $a.snapshot.nativeHandleChanged -or ($a.snapshot|ConvertTo-Json) -like '*CONTROLLED_PRIVATE_*'){throw 'Actual helper did not retain observed changed peer with safe snapshot.'};Pass 'actual-helper-retains-only-changed-owned-edit'
foreach($case in @('foreign-uia-pid','foreign-native-pid','zero-native','zero-kernel','kernel-mismatch','outside-modal','foreign-picker-pid','foreign-foreground','wrong-role','wrong-native-class','same-native-handle','same-uia-identity','zero-baseline-handle','disabled','offscreen','exited-process')){
    Reset-Focus;[System.Windows.Automation.AutomationElement]::FocusedElement=$changed;[CanvasPackageInput]::KernelFocus=[IntPtr]22;$baseHandle=[IntPtr]21
    switch($case){
        'foreign-uia-pid'{$changed.Current.ProcessId=78}
        'foreign-native-pid'{$changed.Current.NativeWindowHandle=99;[CanvasPackageInput]::KernelFocus=[IntPtr]99}
        'zero-native'{$changed.Current.NativeWindowHandle=0}
        'zero-kernel'{[CanvasPackageInput]::KernelFocus=[IntPtr]::Zero}
        'kernel-mismatch'{[CanvasPackageInput]::KernelFocus=[IntPtr]21}
        'outside-modal'{$script:inDialog=$false}
        'foreign-picker-pid'{[CanvasPackageInput]::PickerPid=78}
        'foreign-foreground'{[CanvasPackageInput]::Foreground=$false}
        'wrong-role'{$changed.Current.ControlType.ProgrammaticName='ControlType.Pane'}
        'wrong-native-class'{[CanvasPackageInput]::ClassEdit=$false}
        'same-native-handle'{$changed.Current.NativeWindowHandle=21;[CanvasPackageInput]::KernelFocus=[IntPtr]21}
        'same-uia-identity'{$changed.ControlledIdentity=1}
        'zero-baseline-handle'{$baseHandle=[IntPtr]::Zero}
        'disabled'{$changed.Current.IsEnabled=$false}
        'offscreen'{$changed.Current.IsOffscreen=$true}
        'exited-process'{$process.HasExited=$true}
    }
    $v=Read-OwnedPickerFocus $dialog ([IntPtr]19) $baseline $baseHandle
    if($null -ne $v){throw 'Actual helper admitted foreign/unavailable/unchanged address focus.'}
    if($case -ceq 'foreign-uia-pid' -and $treeCalls -ne 0){throw 'Actual helper walked foreign UIA tree before ownership refusal.'}
    if($case -in @('foreign-uia-pid','foreign-native-pid','zero-native','zero-kernel','kernel-mismatch','outside-modal','foreign-picker-pid','foreign-foreground','disabled','offscreen','exited-process') -and [System.Windows.Automation.AutomationElement]::EqualityCalls -ne 0){throw 'Actual helper compared baseline/runtime identity before basic owned focus admission.'}
    Pass ('actual-helper-refuses-'+$case)
}
$choose=@($ast.FindAll({param($n)$n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Choose-OwnFolder'},$true))[0]
$acquire=@($choose.FindAll({param($n)$n -is [System.Management.Automation.Language.TryStatementAst] -and $n.Extent.Text.Contains('$beforeFocus=Wait-Observed')},$true))[0]
function Wait-Observed([scriptblock]$Probe,[string]$Name){$v=&$Probe;if(-not $v){throw ('CONTROLLED_FOCUS_WAIT_REFUSAL: '+$Name)};return $v}
function Write-Result {}
function Record-PickerControlWitness {}
foreach($case in @('proper-transition','baseline-zero-kernel','baseline-unavailable','transition-unchanged')){
    Reset-Focus;$result=[ordered]@{};$handle=[IntPtr]19;$address=$null;$caught=$null
    switch($case){'baseline-zero-kernel'{[CanvasPackageInput]::KernelFocus=[IntPtr]::Zero};'baseline-unavailable'{[System.Windows.Automation.AutomationElement]::FocusedElement=$null};'transition-unchanged'{[CanvasPackageInput]::NextFocus=$baseline;[CanvasPackageInput]::NextKernelFocus=[IntPtr]21}}
    try{. ([scriptblock]::Create($acquire.Extent.Text))}catch{$caught=$_}
    if($case -ceq 'proper-transition'){
        if($null -ne $caught -or [CanvasPackageInput]::ChordCalls -ne 1 -or [CanvasPackageInput]::FirstKey -ne 0x11 -or [CanvasPackageInput]::SecondKey -ne 0x4C -or -not [object]::ReferenceEquals($address,$changed) -or ($result|ConvertTo-Json -Depth 8) -like '*CONTROLLED_PRIVATE_*'){throw 'Actual acquisition sequence failed single original chord/current changed focus/privacy.'}
    }else{
        if($null -eq $caught -or $null -ne $address){throw 'Actual acquisition did not retain refusal without address selection.'}
        $expectedChordCalls=$(if($case -ceq 'transition-unchanged'){1}else{0})
        if([CanvasPackageInput]::ChordCalls -ne $expectedChordCalls){throw 'Actual input ran before owned baseline or repeated after failed transition.'}
    }
    Pass ('actual-acquisition-order-'+$case)
}
$receipt=[ordered]@{schemaVersion=1;status='PASS';sourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant();parserErrors=$errors.Count;controlCount=$controls.Count;controls=$controls;qualification='Actual new source predicate/reader/acquisition try with controlled Linux CLR/kernel/input signatures only. No installed UIA/native app/provider/package/profile/model/input/authority acceptance. Actual control counts verify source keyboard order only, not OS actuation; existing Wait20s unchanged; unchanged31/47/65 not repeated.'}
$receipt|ConvertTo-Json -Depth 8|Set-Content -Encoding utf8NoBOM (Join-Path $evidence 'actual-focus-transition-controls-receipt.json')
Write-Output ('Actual new focus transition controls PASS: '+$controls.Count)
