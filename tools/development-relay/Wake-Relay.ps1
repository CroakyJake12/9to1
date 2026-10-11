param([Parameter(Mandatory=$true)][string]$Config)
$ErrorActionPreference='Stop'
$state=& node (Join-Path $PSScriptRoot 'wake-state.mjs') $Config | ConvertFrom-Json
if($LASTEXITCODE -ne 0){throw 'Wake policy cannot be verified'}
if($state.home -and -not $state.paused -and $state.permitted){Start-ScheduledTask -TaskName '9to1-Development-Relay'}
