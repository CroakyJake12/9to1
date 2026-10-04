[CmdletBinding()]
param([switch] $Status, [switch] $Launch)

$root = Split-Path -Parent $PSScriptRoot
$vbox = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'
$vmConfig = Get-Content -LiteralPath (Join-Path $root 'vm\havenos-dev.json') -Raw | ConvertFrom-Json
if (-not (Test-Path -LiteralPath $vbox)) { throw "VirtualBox tooling is not installed at $vbox." }
$info = & $vbox showvminfo $vmConfig.uuid --machinereadable
if ($LASTEXITCODE -ne 0) { throw "The configured HavenOS VM $($vmConfig.uuid) is unavailable." }
$state = ($info | Where-Object { $_ -like 'VMState=*' }) -replace '^VMState="|"$'
$snapshot = ($info | Where-Object { $_ -like 'CurrentSnapshotUUID=*' }) -replace '^CurrentSnapshotUUID="|"$'
if ($snapshot -ne $vmConfig.snapshot.uuid) { throw 'Configured VM is not at the approved snapshot. Refusing to launch.' }
if ($Status) { [pscustomobject]@{ Name = $vmConfig.name; Uuid = $vmConfig.uuid; State = $state; Snapshot = $vmConfig.snapshot.name; SnapshotUuid = $snapshot }; exit 0 }
if (-not $Launch) { throw 'Pass -Launch only after verifying the selected HavenOS VM and snapshot.' }
if ($state -notin @('poweroff', 'aborted')) { throw "VM is in '$state' state; refusing an ambiguous launch operation." }
& $vbox startvm $vmConfig.uuid --type gui
if ($LASTEXITCODE -ne 0) { throw 'VirtualBox did not start the approved HavenOS VM.' }
