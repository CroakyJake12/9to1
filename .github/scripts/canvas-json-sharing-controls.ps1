param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$output=[IO.Path]::GetFullPath($OutputDirectory)
if([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)){throw 'Actual runner temporary directory is unavailable.'}
$actualTaskTemp=[IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')
if(-not [IO.Path]::GetDirectoryName($output).Equals($actualTaskTemp,[StringComparison]::OrdinalIgnoreCase)){throw 'Diagnostic output must be a direct owned child of actual runner temporary directory.'}
if(([IO.File]::GetAttributes($actualTaskTemp)-band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Actual temporary directory is redirected.'}
if(Test-Path -LiteralPath $output){throw 'Diagnostic output must be new.'}
[void][IO.Directory]::CreateDirectory($output)
$result=[ordered]@{schemaVersion=1;status='NOT_RUN';stage='preflight';checks=@();workerDrains=@();forcedWorkerCleanup=$false;originalSharing=$null;failure=$null;qualification='Synthetic Windows PowerShell5.1 sharing/atomic replacement observations on task-owned fictional JSON only. No package/app/profile/principal/IDs/provider/GUI/authority/state workflow. Original b0db native failure cause remains unestablished even if a reader-sharing mechanism is observed.'}
function Write-Receipt {$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding UTF8}
function Check([bool]$Passed,[string]$Label){$result.checks+=[ordered]@{name=$Label;passed=$Passed};Write-Receipt;if(-not $Passed){throw 'Controlled diagnostic check refused.'}}
function Quote-WorkerArgument([string]$Argument){
    if($Argument.Contains('"') -or $Argument.EndsWith('\') -or $Argument.Contains("`r") -or $Argument.Contains("`n")){throw 'Controlled file argument refused.'}
    return '"'+$Argument+'"'
}
function Read-NativeAccess([string]$TargetFile){
    $read=[CanvasJsonSharingNative]::OpenAccess($TargetFile,[uint32]2147483648)
    $write=[CanvasJsonSharingNative]::OpenAccess($TargetFile,0x40000000)
    $delete=[CanvasJsonSharingNative]::OpenAccess($TargetFile,0x00010000)
    return [ordered]@{readAccessAllowed=$read.Opened;readError=$read.Error;writeAccessAllowed=$write.Opened;writeError=$write.Error;deleteAccessAllowed=$delete.Opened;deleteError=$delete.Error}
}
function Invoke-FixtureMove([string]$SourceFile,[string]$DestinationFile){
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=(Get-Command dotnet -CommandType Application).Source
    $info.Arguments=(Quote-WorkerArgument $workerDll)+' '+(Quote-WorkerArgument $SourceFile)+' '+(Quote-WorkerArgument $DestinationFile)
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $child=New-Object Diagnostics.Process;$child.StartInfo=$info;$started=$false
    try{
        if(-not $child.Start()){throw 'Controlled worker did not start.'};$started=$true
        $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
        if(-not $child.WaitForExit(5000)){$result.forcedWorkerCleanup=$true;$child.Kill();[void]$child.WaitForExit(5000);throw 'Controlled worker exceeded its bound.'}
        $outComplete=$stdout.Wait(2000);$errComplete=$stderr.Wait(2000)
        $result.workerDrains+=[ordered]@{stdoutCompleted=$outComplete;stderrCompleted=$errComplete;boundMilliseconds=2000;exitCode=$child.ExitCode}
        if(-not $outComplete -or -not $errComplete){throw 'Controlled worker stream drain refused.'}
        if($child.ExitCode -ne 0 -or -not [string]::IsNullOrWhiteSpace($stderr.Result) -or $stdout.Result.Length -gt 4096){throw 'Controlled worker output refused.'}
        return Microsoft.PowerShell.Utility\ConvertFrom-Json -InputObject $stdout.Result
    }finally{
        if($started){try{if(-not $child.HasExited){$result.forcedWorkerCleanup=$true;$child.Kill();if(-not $child.WaitForExit(5000)){$result.workerCleanupFailure='Bounded controlled worker cleanup did not finish.'}}}catch{$result.workerCleanupFailure=$_.Exception.GetType().FullName}}
        $child.Dispose()
    }
}
function Test-ControlledMoveRefusal($Move){
    if($Move.moveCompleted -or -not $Move.overwriteTrue){return $false}
    return (($Move.errorType -ceq 'System.IO.IOException' -and $Move.hresult -eq -2147024864 -and $Move.win32Code -eq 32) -or ($Move.errorType -ceq 'System.UnauthorizedAccessException' -and $Move.hresult -eq -2147024891 -and $Move.win32Code -eq 5))
}
function Test-HeldDeleteShareObservation($Observation){
    if(-not $Observation.access.readAccessAllowed -or $Observation.access.readError -ne 0 -or -not $Observation.access.deleteAccessAllowed -or $Observation.access.deleteError -ne 0){return $false}
    $move=$Observation.move
    if($move.moveCompleted){return $move.overwriteTrue -and $move.errorType -ceq 'NONE' -and $move.hresult -eq 0 -and $move.win32Code -eq 0}
    return Test-ControlledMoveRefusal $move
}
function Test-OriginalSharingObservation($Observation){
    if(-not $Observation.access.readAccessAllowed -or $Observation.access.readError -ne 0){return $false}
    if($Observation.access.deleteAccessAllowed){return $Observation.access.deleteError -eq 0 -and $Observation.move.moveCompleted}
    return $Observation.access.deleteError -eq 32 -and (Test-ControlledMoveRefusal $Observation.move)
}
$workerCode=@'
using System;
using System.IO;
namespace TeamCJsonSharing {
public static class FixtureMoveWorker {
    public static int Main(string[] args) {
        if(args.Length!=2) return 2;
        bool moved=false; int hresult=0; string type="NONE";
        try { File.Move(args[0],args[1],true); moved=true; }
        catch(IOException e) { hresult=e.HResult; type="System.IO.IOException"; }
        catch(UnauthorizedAccessException e) { hresult=e.HResult; type="System.UnauthorizedAccessException"; }
        catch(Exception e) { hresult=e.HResult; type="UnexpectedBoundaryRefusal"; }
        Console.WriteLine("{\"moveCompleted\":"+(moved?"true":"false")+",\"hresult\":"+hresult+",\"win32Code\":"+(hresult & 65535)+",\"errorType\":\""+type+"\",\"overwriteTrue\":true,\"runtimeVersion\":\""+Environment.Version+"\"}");
        return 0;
    }
}}
'@
$nativeCode=@'
using System;
using System.Runtime.InteropServices;
public sealed class CanvasJsonAccessObservation { public bool Opened; public int Error; }
public static class CanvasJsonSharingNative {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr CreateFile(string path,uint access,uint share,IntPtr security,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool CloseHandle(IntPtr handle);
    public static CanvasJsonAccessObservation OpenAccess(string path,uint access) {
        var handle=CreateFile(path,access,7,IntPtr.Zero,3,128,IntPtr.Zero);
        if(handle==new IntPtr(-1)) return new CanvasJsonAccessObservation { Opened=false,Error=Marshal.GetLastWin32Error() };
        try { return new CanvasJsonAccessObservation { Opened=true,Error=0 }; }
        finally { if(!CloseHandle(handle)) throw new InvalidOperationException("Controlled metadata handle close refused."); }
    }
}
'@
$exitCode=1
try{
    Check ($env:OS -ceq 'Windows_NT' -and [IntPtr]::Size -eq 8 -and $PSVersionTable.PSEdition -ceq 'Desktop' -and $PSVersionTable.PSVersion.Major -eq 5) 'Actual Windows x64 PowerShell5.1 host'
    $result.powerShellVersion=$PSVersionTable.PSVersion.ToString();$result.clrVersion=$PSVersionTable.CLRVersion.ToString()
    $drive=New-Object IO.DriveInfo([IO.Path]::GetPathRoot($output));$result.fileSystem=$drive.DriveFormat
    Check ($drive.DriveFormat -ceq 'NTFS') 'Own diagnostic fixture is on actual NTFS'
    $readerSource=Join-Path $PSScriptRoot 'canvas-packaged-ui-controls.ps1'
    $result.originalNativeScriptSha256=(Get-FileHash -LiteralPath $readerSource -Algorithm SHA256).Hash.ToLowerInvariant()
    Check ($result.originalNativeScriptSha256 -ceq 'f25f7153ed69abeb01057f854164ab6a5bb39354f6ce10f7188e9789da7ba736') 'Exact original b0db native harness body is unchanged'
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($readerSource,[ref]$tokens,[ref]$errors)
    $reader=@($ast.FindAll({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Read-BoundedJson'},$true))
    Check ($errors.Count -eq 0 -and $reader.Count -eq 1) 'Actual original Read-BoundedJson extracted without native invocation'
    . ([scriptblock]::Create($reader[0].Extent.Text))
    $cmd=Get-Command Get-Content -CommandType Cmdlet
    $assembly=$cmd.ImplementingType.Assembly
    $result.getContentRuntime=[ordered]@{commandType=$cmd.CommandType.ToString();implementingType=$cmd.ImplementingType.FullName;assemblyName=$assembly.GetName().Name;assemblyVersion=$assembly.GetName().Version.ToString();publicKeyToken=([BitConverter]::ToString($assembly.GetName().GetPublicKeyToken())).Replace('-','').ToLowerInvariant();globalAssemblyCache=$assembly.GlobalAssemblyCache;assemblySha256=(Get-FileHash -LiteralPath $assembly.Location -Algorithm SHA256).Hash.ToLowerInvariant();beneathPowerShellHome=[IO.Path]::GetFullPath($assembly.Location).StartsWith([IO.Path]::GetFullPath($PSHOME)+'\',[StringComparison]::OrdinalIgnoreCase);beneathWindowsDirectory=[IO.Path]::GetFullPath($assembly.Location).StartsWith([IO.Path]::GetFullPath($env:windir)+'\',[StringComparison]::OrdinalIgnoreCase)}
    Check ($cmd.ImplementingType.FullName -ceq 'Microsoft.PowerShell.Commands.GetContentCommand' -and $result.getContentRuntime.assemblyName -ceq 'Microsoft.PowerShell.Commands.Management' -and $result.getContentRuntime.publicKeyToken -ceq '31bf3856ad364e35' -and ($result.getContentRuntime.beneathPowerShellHome -or ($result.getContentRuntime.globalAssemblyCache -and $result.getContentRuntime.beneathWindowsDirectory))) 'Actual installed Microsoft Get-Content runtime metadata observed'
    Add-Type -TypeDefinition $nativeCode -Language CSharp
    $work=Join-Path $output 'synthetic-worker';[void][IO.Directory]::CreateDirectory($work)
    $project=Join-Path $work 'FixtureMoveWorker.csproj';$nuget=Join-Path $work 'NuGet.Config'
    [IO.File]::WriteAllText($project,'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $work 'Program.cs'),$workerCode)
    [IO.File]::WriteAllText($nuget,'<configuration><packageSources><clear /></packageSources></configuration>')
    $workerOutput=Join-Path $work 'out'
    & dotnet build $project --configuration Release --output $workerOutput ('-p:RestoreConfigFile='+$nuget) --nologo --verbosity quiet *> (Join-Path $work 'build-private-task.log')
    Check ($LASTEXITCODE -eq 0) 'Actual existing .NET10 SDK compiled same File.Move overwrite API without package sources'
    $workerDll=Join-Path $workerOutput 'FixtureMoveWorker.dll'
    $utf8=New-Object Text.UTF8Encoding($false);$oldJson='{"control":1}';$newJson='{"control":2}'
    $result.stage='deterministic-held-reader-controls'
    $target=Join-Path $output 'held-no-delete.json';$replacement=Join-Path $output 'held-no-delete-new.json'
    [IO.File]::WriteAllText($target,$oldJson,$utf8);[IO.File]::WriteAllText($replacement,$newJson,$utf8)
    $stream=New-Object IO.FileStream($target,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    try{$access=Read-NativeAccess $target;$move=Invoke-FixtureMove $replacement $target;$result.noDeleteControl=[ordered]@{access=$access;move=$move};Check (-not $access.deleteAccessAllowed -and $access.deleteError -eq 32 -and (Test-ControlledMoveRefusal $move)) 'Held reader without Delete sharing refuses kernel DELETE32 and exact genuine overwrite refusal'}finally{$stream.Dispose()}
    Check ([IO.File]::ReadAllText($target) -ceq $oldJson -and [IO.File]::Exists($replacement)) 'Refused overwrite preserves both controlled old primary and new source'
    $target=Join-Path $output 'held-with-delete.json';$replacement=Join-Path $output 'held-with-delete-new.json'
    [IO.File]::WriteAllText($target,$oldJson,$utf8);[IO.File]::WriteAllText($replacement,$newJson,$utf8)
    $stream=New-Object IO.FileStream($target,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $textReader=New-Object IO.StreamReader($stream,[Text.Encoding]::UTF8,$true)
    try{$access=Read-NativeAccess $target;$move=Invoke-FixtureMove $replacement $target;$oldText=$textReader.ReadToEnd();$result.deleteShareControl=[ordered]@{access=$access;move=$move}}finally{$textReader.Dispose();$stream.Dispose()}
    Check (-not $stream.CanRead -and (Test-HeldDeleteShareObservation $result.deleteShareControl)) 'Explicit ReadWrite Delete sharing outcome is exact and stream closes before JSON parse'
    $parsed=Microsoft.PowerShell.Utility\ConvertFrom-Json -InputObject $oldText
    $expectedPrimary=if($move.moveCompleted){$newJson}else{$oldJson}
    Check ($parsed.control -eq 1 -and [IO.File]::ReadAllText($target) -ceq $expectedPrimary -and [IO.File]::Exists($replacement) -eq (-not $move.moveCompleted)) 'Closed retained old stream parses old complete JSON and exact observed source and primary are preserved'
    $result.heldDeleteShareOverwriteObstructionObserved=-not $move.moveCompleted
    # A consumed fictional source is recreated only if the held overwrite already succeeded.
    # A refused source is retained unchanged for the same API after the handle closes.
    if($move.moveCompleted){[IO.File]::WriteAllText($replacement,$newJson,$utf8)}
    $closedAccess=Read-NativeAccess $target;$closedMove=Invoke-FixtureMove $replacement $target
    $result.deleteShareAfterClose=[ordered]@{access=$closedAccess;move=$closedMove;sourceRecreatedBecauseConsumed=$move.moveCompleted}
    Check ($closedAccess.readAccessAllowed -and $closedAccess.readError -eq 0 -and $closedAccess.deleteAccessAllowed -and $closedAccess.deleteError -eq 0 -and $closedMove.moveCompleted -and $closedMove.overwriteTrue -and $closedMove.errorType -ceq 'NONE' -and $closedMove.hresult -eq 0 -and $closedMove.win32Code -eq 0 -and [IO.File]::ReadAllText($target) -ceq $newJson -and -not [IO.File]::Exists($replacement)) 'Same genuine overwrite completes after controlled reader closes with complete expected primary and consumed source'
    $result.stage='actual-original-get-content-pipeline'
    $script:pipelineTarget=Join-Path $output 'original-pipeline.json';$script:pipelineSource=Join-Path $output 'original-pipeline-new.json'
    [IO.File]::WriteAllText($script:pipelineTarget,$oldJson,$utf8);[IO.File]::WriteAllText($script:pipelineSource,$newJson,$utf8);$script:pipelineCalls=0
    function ConvertFrom-Json {
        [CmdletBinding()]param([Parameter(ValueFromPipeline=$true)]$InputObject)
        process{
            $script:pipelineCalls++
            if($script:pipelineCalls -ne 1){throw 'Original pipeline yielded unexpected repeated object.'}
            $result.originalSharing=[ordered]@{access=(Read-NativeAccess $script:pipelineTarget);move=(Invoke-FixtureMove $script:pipelineSource $script:pipelineTarget);pipelineObjectIsString=$InputObject -is [string]}
            Microsoft.PowerShell.Utility\ConvertFrom-Json -InputObject $InputObject
        }
    }
    try{$parsed=Read-BoundedJson $script:pipelineTarget}finally{Remove-Item -LiteralPath Function:\ConvertFrom-Json}
    Check ($script:pipelineCalls -eq 1 -and $result.originalSharing.pipelineObjectIsString -and $parsed.control -eq 1 -and (Test-OriginalSharingObservation $result.originalSharing)) 'Actual original Get-Content pipeline lifetime and sharing observation is internally consistent'
    $accessAfter=Read-NativeAccess $script:pipelineTarget;$result.afterOriginalPipeline=$accessAfter
    Check $accessAfter.deleteAccessAllowed 'Actual original reader is closed after the full pipeline returns'
    $afterSource=Join-Path $output 'after-pipeline-new.json';[IO.File]::WriteAllText($afterSource,$newJson,$utf8);$afterMove=Invoke-FixtureMove $afterSource $script:pipelineTarget;$result.afterOriginalPipelineMove=$afterMove
    Check ($afterMove.moveCompleted) 'Same overwrite succeeds after actual original pipeline reader closes'
    $result.originalReaderReplacementObstructionObserved=-not $result.originalSharing.access.deleteAccessAllowed -and $result.originalSharing.access.deleteError -eq 32 -and (Test-ControlledMoveRefusal $result.originalSharing.move)
    $result.originalNativeFailureCause='UNESTABLISHED_BY_SYNTHETIC_PROBE'
    Check (-not $result.forcedWorkerCleanup -and -not $result.Contains('workerCleanupFailure') -and @($result.workerDrains | Where-Object {-not $_.stdoutCompleted -or -not $_.stderrCompleted -or $_.exitCode -ne 0}).Count -eq 0) 'All controlled workers exit naturally with bounded complete drains'
    $result.status='SYNTHETIC_WINDOWS_SHARING_OBSERVATIONS_COMPLETE_NATIVE_CAUSE_UNESTABLISHED';$result.stage='complete';$exitCode=0
}catch{$result.status='DIAGNOSTIC_FAILED_UNACCEPTED';$result.failure=[ordered]@{type=$_.Exception.GetType().FullName;stage=$result.stage;rawExceptionText='WITHHELD'}}finally{Write-Receipt}
exit $exitCode
