param([Parameter(Mandatory=$true)][string]$Config,[Parameter(Mandatory=$true)][string]$Node)
$ErrorActionPreference='Stop'
$taskName='9to1-Development-Relay'
$runner=Join-Path $PSScriptRoot 'runner.mjs'
$configPath=(Resolve-Path -LiteralPath $Config).Path
$nodePath=(Resolve-Path -LiteralPath $Node).Path
$args='"'+$runner+'" "'+$configPath+'"'
$existing=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if($existing){
    if($existing.Actions.Execute -ne $nodePath -or $existing.Actions.Arguments -ne $args){throw 'Existing task has different ownership/action; refusing overwrite.'}
    Write-Output 'Matching relay scheduler already installed.'
}else{
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action=New-ScheduledTaskAction -Execute $nodePath -Argument $args -WorkingDirectory $PSScriptRoot
    $login=New-ScheduledTaskTrigger -AtLogOn -User $identity
    $retry=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5)
    $principal=New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
    $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 5) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($login,$retry) -Principal $principal -Settings $settings | Out-Null
    Write-Output 'Installed user-session relay task with login and five-minute recovery triggers.'
}
Start-ScheduledTask -TaskName $taskName
Get-ScheduledTask -TaskName $taskName | Select-Object TaskName,State
Get-ScheduledTaskInfo -TaskName $taskName | Select-Object LastRunTime,LastTaskResult,NextRunTime
