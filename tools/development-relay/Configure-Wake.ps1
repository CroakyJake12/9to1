param([Parameter(Mandatory=$true)][string]$Config,[switch]$SyncOnly)
$ErrorActionPreference='Stop'
$name='9to1-Relay-Wake'
$configPath=(Resolve-Path -LiteralPath $Config).Path
$settings=Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$powershell=$settings.powershell
$script=Join-Path $PSScriptRoot 'Wake-Relay.ps1'
$arguments='-NoProfile -File "'+$script+'" -Config "'+$configPath+'"'
$existing=Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
if($existing -and ($existing.Actions.Execute -ne $powershell -or $existing.Actions.Arguments -ne $arguments)){throw 'Existing wake task belongs to another action; not changed'}
if(-not $SyncOnly){
    if((Get-TimeZone).Id -ne 'GMT Standard Time'){throw 'Windows timezone is not London; no global timezone change was made'}
    $triggers=@()
    foreach($day in @('Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday')){
        $rule=$settings.schedule.weekdays.$day
        if($rule.disabled){continue}
        $start=[TimeSpan]::ParseExact($rule.local_start,'hh\:mm',[cultureinfo]::InvariantCulture)
        $college=$settings.schedule.school_term -and $day -notin @('Saturday','Sunday')
        if($college -and $settings.schedule.rules.no_local_development_before_college){
            $last=[TimeSpan]::ParseExact($rule.final_non_EPQ_class_end,'hh\:mm',[cultureinfo]::InvariantCulture)+[TimeSpan]::FromMinutes($settings.schedule.rules.commute_minutes_each_way)
            if($last -gt $start){$start=$last}
        }
        if($start -ge [TimeSpan]::ParseExact($rule.local_end,'hh\:mm',[cultureinfo]::InvariantCulture)){continue}
        $trigger=New-ScheduledTaskTrigger -Weekly -DaysOfWeek $day -At ([datetime]::Today+$start)
        # Floating Windows local time follows GMT Standard Time's London DST rules.
        $trigger.StartBoundary=([datetime]::Today+$start).ToString('yyyy-MM-ddTHH:mm:ss',[cultureinfo]::InvariantCulture)
        $triggers+=$trigger
    }
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name
    $action=New-ScheduledTaskAction -Execute $powershell -Argument $arguments -WorkingDirectory $PSScriptRoot
    $principal=New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
    $options=New-ScheduledTaskSettingsSet -WakeToRun -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 2)
    Register-ScheduledTask -TaskName $name -Action $action -Trigger $triggers -Principal $principal -Settings $options -Force | Out-Null
}
if(-not $existing -and $SyncOnly){exit 0}
$state=& node (Join-Path $PSScriptRoot 'wake-state.mjs') $configPath | ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Wake availability cannot be verified'}
if($state.home -and -not $state.paused -and $settings.pc_wake_enabled){Enable-ScheduledTask -TaskName $name | Out-Null}else{Disable-ScheduledTask -TaskName $name | Out-Null}
Get-ScheduledTask -TaskName $name | Select-Object TaskName,State,@{Name='WakeToRun';Expression={$_.Settings.WakeToRun}}
