[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ExpectedCommit,[Parameter(Mandatory=$true)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$repo=(& git rev-parse --show-toplevel).Trim()
Set-Location -LiteralPath $repo
$output=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'A fresh owned output is required.' }
if ($output.Equals($repo,[StringComparison]::OrdinalIgnoreCase) -or $output.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Outputs must be outside source.' }
$diagnostics=Join-Path $output 'diagnostics';New-Item -ItemType Directory -Path $diagnostics | Out-Null
$catalog=Get-Content -LiteralPath '.github/validation/home-windows-core.json' -Raw | ConvertFrom-Json
$result=[ordered]@{schemaVersion=1;status='NOT_RUN';stage='source';workflowCommit=$ExpectedCommit;runId=$env:GITHUB_RUN_ID;runAttempt=$env:GITHUB_RUN_ATTEMPT;sourceBasis=$catalog.sourceBasis;publicOwnerCommit=$catalog.publicOwnerCommit;currentOwnerSources=$catalog.currentOwnerSources;denominator=$catalog.denominator;commands=@();managedControls=@();sourceAfterUnchanged=$false;qualification=$catalog.qualification;acceptanceVerified=$false;ownerAdoption='UNACCEPTED'}
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
function Assert-OriginalIdentities($Names,[switch]$Focused) {
    $unique=@($Names | Sort-Object -Unique)
    $wanted=if($Focused){[int]$catalog.focusedCases}else{[int]$catalog.owningCases}
    if($Names.Count -ne $wanted -or $unique.Count -ne $wanted){throw 'Original case count or identity uniqueness differs.'}
    foreach($group in $catalog.windowsGroups){
        $matched=@($Names | Where-Object{$_ -ceq $group.method -or $_.StartsWith($group.method+'(',[StringComparison]::Ordinal)})
        if($matched.Count -ne [int]$group.cases){throw 'An original Windows case group is missing or replaced.'}
    }
    if(-not $Focused){foreach($name in $catalog.originalFactNames){if($Names -cnotcontains $name){throw 'An original owning Fact identity is missing.'}}}
    if(-not $Focused){foreach($group in $catalog.additionalGroups){
        $matched=@($Names|Where-Object{if($group.kind.EndsWith('Theory',[StringComparison]::Ordinal)){$_.StartsWith($group.method+'(',[StringComparison]::Ordinal)}else{$_ -ceq $group.method}})
        if($matched.Count -ne [int]$group.cases){throw 'An exact current PR14/PR15 original case group is missing or replaced.'}
    }}
}
function Run-OriginalCases([string]$Configuration,[switch]$Focused) {
    $scope=if($Focused){'focused12'}else{'whole113'};$label=$Configuration+'-'+$scope
    $project=$catalog.owningProject;$selection=@();if($Focused){$selection=@('--filter',$catalog.focusedFilter)}
    $control=[ordered]@{cohort=$label;configuration=$Configuration;project=$project;filter=if($Focused){$catalog.focusedFilter}else{''};requiredCases=if($Focused){[int]$catalog.focusedCases}else{[int]$catalog.owningCases};status='NOT_RUN';total=0;executed=0;passed=0;failed=0;skipped=0;names=@();assertionCount=$null}
    $runtime=$null;$runtimeBefore=$null
    try{
        $assembly=(Invoke-DotNet ($label+'-target-path') @('msbuild',$project,('-p:Configuration='+$Configuration),'-p:UseArtifactsOutput=true',("-p:ArtifactsPath=$artifacts"),$taskFlag,$avaloniaTaskFlag,'-getProperty:TargetPath')).Trim()
        if(-not(Test-Path -LiteralPath $assembly -PathType Leaf)){throw 'Actual source-built owning assembly is absent.'}
        $runtime=[IO.Path]::GetDirectoryName($assembly);$runtimeBefore=@(Get-FileCatalog $runtime)
        Write-Json (Join-Path $diagnostics ($label+'-runtime-before.json')) $runtimeBefore
        $control.assemblySHA256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()
        $discovery=Invoke-DotNet ($label+'-discovery') (@('test',$project,'--no-build','--no-restore','-c',$Configuration,'--list-tests',$taskFlag)+$selection+$flags)
        $names=@($discovery -split '\r?\n' | ForEach-Object{$_.Trim()} | Where-Object{$_.StartsWith('HavenOS.Home.Tests.',[StringComparison]::Ordinal)})
        Write-Json (Join-Path $diagnostics ($label+'-discovery.json')) ([ordered]@{total=$names.Count;names=$names;filter=$control.filter})
        Assert-OriginalIdentities $names -Focused:$Focused
        $tests=Join-Path $diagnostics ($label+'-test-results');New-Item -ItemType Directory -Path $tests | Out-Null
        [void](Invoke-DotNet ($label+'-test') (@('test',$project,'--no-build','--no-restore','-c',$Configuration,'--logger',('trx;LogFileName='+$label+'.trx'),'--results-directory',$tests,$taskFlag)+$selection+$flags) -RetainFailure)
        [xml]$trx=Get-Content -LiteralPath (Join-Path $tests ($label+'.trx')) -Raw
        $ns=[Xml.XmlNamespaceManager]::new($trx.NameTable);$ns.AddNamespace('t','http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $counter=$trx.SelectSingleNode('//t:Counters',$ns);$cases=@($trx.SelectNodes('//t:UnitTestResult',$ns))
        $control.total=[int]$counter.total;$control.executed=[int]$counter.executed;$control.passed=[int]$counter.passed;$control.failed=[int]$counter.failed;$control.skipped=[int]$counter.notExecuted;$control.names=@($cases|ForEach-Object{$_.testName})
        Assert-OriginalIdentities $control.names -Focused:$Focused
        if((ConvertTo-Json -InputObject @($names|Sort-Object) -Compress) -cne (ConvertTo-Json -InputObject @($control.names|Sort-Object) -Compress)){throw 'Discovery and original TRX identity sets differ.'}
        if($control.total -ne $control.requiredCases -or $control.executed -ne $control.requiredCases -or $control.passed -ne $control.requiredCases -or $control.failed -ne 0 -or $control.skipped -ne 0 -or $cases.Count -ne $control.requiredCases -or @($cases|Where-Object{$_.outcome -cne 'Passed'}).Count -ne 0){throw 'Every original required control must execute and pass without skips.'}
        if(@($result.commands|Where-Object{$_.name -ceq ($label+'-test') -and $_.exitCode -ne 0}).Count -gt 0){throw 'Original process failed despite counters.'}
        $control.status='PASS'
    }catch{$control.status='FAIL_OR_INCOMPLETE';$control.failureType=$_.Exception.GetType().FullName;$control.scriptLine=$_.InvocationInfo.ScriptLineNumber;$control.rawExceptionText='WITHHELD'}
    finally{
        try{if($null -ne $runtimeBefore){$runtimeAfter=@(Get-FileCatalog $runtime -Exclusive);Write-Json (Join-Path $diagnostics ($label+'-runtime-after.json')) $runtimeAfter;if(-not(Same-Catalog $runtimeBefore $runtimeAfter)){throw 'Actual owning runtime changed.'}}}catch{$control.status='FAIL_OR_INCOMPLETE';$control.runtimeCustodyFailureType=$_.Exception.GetType().FullName}
        $result.managedControls+=$control;Save-Result
    }
}
$before=$null
try{
    if(-not[OperatingSystem]::IsWindows() -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64){throw 'Actual Windows x64 required.'}
    if((& git rev-parse HEAD).Trim() -cne $ExpectedCommit){throw 'Exact reviewed producer source required.'}
    & git merge-base --is-ancestor $catalog.sourceBasis HEAD;if($LASTEXITCODE -ne 0){throw 'Wrong source lineage.'}
    foreach($path in @(& git diff --name-only $catalog.sourceBasis HEAD)){if($catalog.harnessPaths -cnotcontains $path){throw 'Undeclared production/test/source change.'}}
    foreach($pin in $catalog.sourcePins){$physical=Join-Path $repo $pin.path;if((Get-Item -LiteralPath $physical).Length -ne $pin.bytes -or (Get-FileHash -LiteralPath $physical -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pin.sha256){throw 'Actual source body pin mismatch.'}}
    foreach($link in $catalog.requiredGitlinks){if((& git -C $link.path rev-parse HEAD).Trim() -cne $link.commit -or $LASTEXITCODE -ne 0){throw 'Actual two-link dependency differs.'}}
    if(@(& git status --porcelain).Count -ne 0){throw 'Actual checkout is not clean.'}
    $available=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($output)).AvailableFreeSpace;$result.initialOutputDriveFreeBytes=$available;if($available -lt 8GB){throw '8GiB disposable output-drive capacity required.'}
    $before=@(Get-TreeSnapshot);Write-Json (Join-Path $diagnostics 'source-before.json') $before
    foreach($pair in @(@('DOTNET_CLI_HOME','cli'),@('NUGET_PACKAGES','nuget'),@('NUGET_HTTP_CACHE_PATH','http'),@('NUGET_PLUGINS_CACHE_PATH','plugins'),@('TMP','tmp'),@('TEMP','tmp'))){$privatePath=Join-Path $output $pair[1];New-Item -ItemType Directory -Path $privatePath -Force | Out-Null;[Environment]::SetEnvironmentVariable($pair[0],$privatePath)}
    if((Invoke-DotNet 'sdk-version' @('--version')).Trim() -cne $catalog.sdk){throw 'Declared SDK identity differs.'}
    $artifacts=Join-Path $output 'artifacts';$flags=@('--artifacts-path',$artifacts,'-p:UseSharedCompilation=false','--disable-build-servers','-m:1','-nodeReuse:false')
    $tasksProject = 'framework/CUI/Compiler/CakeOS.Cui.Build.Tasks/CakeOS.Cui.Build.Tasks.csproj'; $result.stage = 'current-source-tasks'
    [void](Invoke-DotNet 'cui-tasks-release' (@('build',$tasksProject,'-c','Release','--nologo') + $flags))
    $taskProperties = Invoke-DotNet 'tasks-properties' @('msbuild',$tasksProject,'-p:Configuration=Release','-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$artifacts",'-getProperty:TargetPath,TargetFramework') | ConvertFrom-Json
    $tasks = $taskProperties.Properties.TargetPath; if (-not (Test-Path -LiteralPath $tasks -PathType Leaf)) { throw 'Actual source-built Task DLL is absent.' }
    $result.taskAssemblySHA256 = (Get-FileHash -LiteralPath $tasks -Algorithm SHA256).Hash.ToLowerInvariant(); $taskFlag = "-p:CuiBuildTasksLocation=$tasks"
    $result.stage = 'current-source-avalonia-tasks'
    $avaloniaTasksProject = 'framework/CUI/vendor/Avalonia/src/Avalonia.Build.Tasks/Avalonia.Build.Tasks.csproj'
    $avaloniaTaskProperties = Invoke-DotNet 'avalonia-tasks-target-properties' @('msbuild',$avaloniaTasksProject,'-p:Configuration=Release','-p:UseArtifactsOutput=true',"-p:ArtifactsPath=$artifacts",$taskFlag,'-getProperty:TargetPath,TargetFramework') | ConvertFrom-Json
    $avaloniaTasks = $avaloniaTaskProperties.Properties.TargetPath
    if (-not [IO.Path]::IsPathFullyQualified($avaloniaTasks) -or -not $avaloniaTasks.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Actual evaluated Avalonia task must be within owned artifacts.' }
    [void](Invoke-DotNet 'avalonia-tasks-release' (@('build',$avaloniaTasksProject,'-c','Release',$taskFlag,'--nologo') + $flags))
    if (-not (Test-Path -LiteralPath $avaloniaTasks -PathType Leaf)) { throw 'Actual source-built Avalonia task DLL is absent.' }
    $result.avaloniaBuildTaskPath = $avaloniaTasks
    $result.avaloniaBuildTaskSHA256 = (Get-FileHash -LiteralPath $avaloniaTasks -Algorithm SHA256).Hash.ToLowerInvariant()
    $avaloniaTaskFlag = "-p:AvaloniaBuildTasksLocation=$avaloniaTasks"
    $flags += $avaloniaTaskFlag
    $result.stage='original-Windows12-and-unfiltered-owning113'
    foreach($configuration in @('Debug','Release')){
        $project=$catalog.owningProject
        [void](Invoke-DotNet ($configuration+'-restore') (@('restore',$project,'--configfile','NuGet.Config','--disable-parallel',('-p:Configuration='+$configuration),$taskFlag)+$flags))
        [void](Invoke-DotNet ($configuration+'-build') (@('build',$project,'--no-restore','-c',$configuration,$taskFlag)+$flags))
        foreach($node in $catalog.projectGraph.PSObject.Properties.Name){
            $label=$configuration+'-evaluated-'+[Array]::IndexOf(@($catalog.projectGraph.PSObject.Properties.Name),$node)
            [void](Invoke-DotNet $label @('msbuild',$node,('-p:Configuration='+$configuration),'-p:UseArtifactsOutput=true',("-p:ArtifactsPath=$artifacts"),$taskFlag,$avaloniaTaskFlag,'-getProperty:TargetPath,TargetFramework','-getItem:Compile,ProjectReference,PackageReference'))
        }
        Run-OriginalCases $configuration -Focused
        Run-OriginalCases $configuration
    }
    if($result.managedControls.Count -ne 4 -or @($result.managedControls|Where-Object{$_.status -cne 'PASS'}).Count -ne 0){throw 'Original twelve and full current113 Debug/Release outcomes incomplete.'}
    $result.status='PASS_ORIGINAL_WINDOWS_TRANSPORT_AND_WHOLE_HOME_SOURCE_BUILD_UNACCEPTED'
}catch{$result.status='FAIL_OR_INCOMPLETE';$result.failure=[ordered]@{type=$_.Exception.GetType().FullName;stage=$result.stage;scriptLine=$_.InvocationInfo.ScriptLineNumber;rawExceptionText='WITHHELD'}}
finally{
    try{$after=@(Get-TreeSnapshot);Write-Json (Join-Path $diagnostics 'source-after.json') $after;if($null -ne $before){$result.sourceAfterUnchanged=Same-Catalog $before $after};if(-not$result.sourceAfterUnchanged -or @(& git status --porcelain).Count -ne 0){throw 'Current source custody not preserved.'};if($result.Contains('taskAssemblySHA256')){if((Get-FileHash -LiteralPath $tasks -Algorithm SHA256).Hash.ToLowerInvariant() -cne $result.taskAssemblySHA256 -or (Get-FileHash -LiteralPath $avaloniaTasks -Algorithm SHA256).Hash.ToLowerInvariant() -cne $result.avaloniaBuildTaskSHA256){throw 'Actual compiler Task assemblies changed.'}}}catch{$result.status='FAIL_OR_INCOMPLETE';$result.finalCustodyFailureType=$_.Exception.GetType().FullName}
    try{Save-Result}catch{$result.status='FAIL_OR_INCOMPLETE';Write-Error 'Final safe producer evidence write failed.' -ErrorAction Continue}
}
if($result.status -cne 'PASS_ORIGINAL_WINDOWS_TRANSPORT_AND_WHOLE_HOME_SOURCE_BUILD_UNACCEPTED' -or -not$result.sourceAfterUnchanged){exit 1}
exit 0
