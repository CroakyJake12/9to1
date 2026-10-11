param([Parameter(Mandatory=$true)][string]$Config,[Parameter(Mandatory=$true)][int]$Fence)
$ErrorActionPreference='Stop'
$settings=Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
& node (Join-Path $PSScriptRoot 'relayctl.mjs') check-local $Config $Fence
if($LASTEXITCODE -ne 0){throw 'Strata startup blocked by schedule or exclusive ownership.'}
$baseline=Get-Content -LiteralPath $settings.supplied_launcher -Raw
# Extract only the supplied server startup block. Preserve every setup flag and path.
$first=$baseline.IndexOf('Set-Location -LiteralPath $strataRoot')
$last=$baseline.IndexOf('$strataLaunchArgs = @(')
if($first -lt 0 -or $last -le $first){throw 'Supplied launcher shape changed; review required.'}
$portOwners=@(Get-NetTCPConnection -LocalPort 8081 -State Listen -ErrorAction SilentlyContinue)
if($portOwners.Count -gt 0){
    foreach($owner in ($portOwners.OwningProcess | Select-Object -Unique)){
        $p=Get-CimInstance Win32_Process -Filter "ProcessId=$owner"
        if($p.CommandLine -notlike "*$($settings.strata_root)*serve*server.py*"){throw "Port 8081 belongs to an unknown process: $owner; left untouched."}
    }
    $health=Invoke-RestMethod -Uri 'http://127.0.0.1:8081/health' -TimeoutSec 10
    if($health.loaded -and $health.model -match 'iq2_xs' -and [int]$health.max_context -eq 524288){Write-Output 'Existing healthy Strata reused';exit 0}
    throw 'Known Strata process is not ready; preserve it and inspect logs before any restart.'
}
$strataRoot=$settings.strata_root
$strataData=$settings.strata_data
$strataBase='http://127.0.0.1:8081'
. ([scriptblock]::Create($baseline.Substring($first,$last-$first)))
if(-not $strataReady){throw 'Strata readiness not established.'}
