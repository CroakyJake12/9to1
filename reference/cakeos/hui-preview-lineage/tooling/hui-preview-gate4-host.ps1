[CmdletBinding()]
param(
    [switch] $StatusOnly
)

$ErrorActionPreference = 'Stop'

$ExpectedVmUuid = '1c55da8e-b74d-43ae-894b-521f7a64c11e'
$ExpectedSnapshotUuid = 'f25b7456-c562-4ba4-9798-5814ee17fe25'
$AcceptedRunId = '34157071097'
$AcceptedArtifactName = 'hui-linux-graphical-package'
$AcceptedPackageName = 'haven-hui-preview_0.1.0+git2a578502c319_amd64.deb'
$AcceptedSha256 = '104f8ff3929b8b0e1c90673804d7cfbe79a31ea5c2763dc040246a7ead53575a'
$TransferShareName = 'havenos-transfer'

$Root = Split-Path -Parent $PSScriptRoot
$VmConfigPath = Join-Path $Root 'vm\havenos-dev.json'
$VBoxManage = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'

if (-not (Test-Path -LiteralPath $VBoxManage -PathType Leaf)) {
    throw "VirtualBox tooling is not installed at $VBoxManage."
}
if (-not (Test-Path -LiteralPath $VmConfigPath -PathType Leaf)) {
    throw "VM config is missing: $VmConfigPath"
}

$VmConfig = Get-Content -LiteralPath $VmConfigPath -Raw | ConvertFrom-Json
if ($VmConfig.uuid -ne $ExpectedVmUuid) {
    throw "VM config UUID changed from the Gate 4 accepted UUID. Expected $ExpectedVmUuid, got $($VmConfig.uuid)."
}
if ($VmConfig.snapshot.uuid -ne $ExpectedSnapshotUuid) {
    throw "VM config snapshot changed from the Gate 4 accepted snapshot. Expected $ExpectedSnapshotUuid, got $($VmConfig.snapshot.uuid)."
}

$Info = & $VBoxManage showvminfo $ExpectedVmUuid --machinereadable
if ($LASTEXITCODE -ne 0) {
    throw "Approved VM $ExpectedVmUuid is unavailable."
}

function Get-MachineValue([string] $Name) {
    $prefix = "$Name="
    $line = $Info | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) } | Select-Object -First 1
    if ($null -eq $line) { return $null }
    $value = $line.Substring($prefix.Length)
    if ($value.Length -ge 2 -and $value[0] -eq '"' -and $value[$value.Length - 1] -eq '"') {
        $value = $value.Substring(1, $value.Length - 2)
    }
    return $value
}

$VmState = Get-MachineValue 'VMState'
$Snapshot = Get-MachineValue 'CurrentSnapshotUUID'
$Nic1 = Get-MachineValue 'nic1'
$Cable1 = Get-MachineValue 'cableconnected1'

if ($VmState -ne 'running') {
    throw "Gate 4 requires the already-approved VM to be running; current state is '$VmState'."
}
if ($Snapshot -ne $ExpectedSnapshotUuid) {
    throw "Approved VM is no longer at snapshot $ExpectedSnapshotUuid; refusing Gate 4 staging."
}
if ($Nic1 -ne 'nat' -or $Cable1 -ne 'on') {
    throw "Gate 4 network baseline changed (nic1='$Nic1', cableconnected1='$Cable1'); refusing to modify it."
}

$ShareNameLine = $Info | Where-Object {
    $_ -match '^SharedFolderNameMachineMapping([0-9]+)="havenos-transfer"$'
} | Select-Object -First 1
if ($null -eq $ShareNameLine) {
    throw "Existing '$TransferShareName' VirtualBox shared folder is not configured. This script will not create one."
}
$ShareIndex = [regex]::Match($ShareNameLine, '^SharedFolderNameMachineMapping([0-9]+)=').Groups[1].Value
$SharePathRaw = Get-MachineValue "SharedFolderPathMachineMapping$ShareIndex"
if ([string]::IsNullOrWhiteSpace($SharePathRaw)) {
    throw "Existing '$TransferShareName' mapping has no host path."
}
$SharePath = $SharePathRaw.Replace('\\', '\')
if (-not (Test-Path -LiteralPath $SharePath -PathType Container)) {
    throw "Existing '$TransferShareName' host path does not exist: $SharePath"
}

$Status = [ordered]@{
    VmUuid = $ExpectedVmUuid
    VmState = $VmState
    SnapshotUuid = $Snapshot
    Nic1 = $Nic1
    CableConnected1 = $Cable1
    TransferShare = $TransferShareName
    TransferHostPath = $SharePath
    AcceptedRunId = $AcceptedRunId
    AcceptedPackage = $AcceptedPackageName
    AcceptedSha256 = $AcceptedSha256
}

if ($StatusOnly) {
    [pscustomobject] $Status
    exit 0
}

$Gh = Get-Command gh.exe -ErrorAction SilentlyContinue
if ($null -eq $Gh) {
    throw 'GitHub CLI is not installed; refusing to rebuild the accepted artifact locally.'
}
& $Gh.Source auth status -h github.com *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not authenticated; refusing to use an unverified alternate download path.'
}

$ArtifactDirectory = Join-Path $env:TEMP 'cakeos-hui-gate4-2a578502c319'
$PackagePath = Join-Path $ArtifactDirectory $AcceptedPackageName
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    if (Test-Path -LiteralPath $ArtifactDirectory) {
        $unexpected = Get-ChildItem -LiteralPath $ArtifactDirectory -Force -ErrorAction SilentlyContinue
        if ($unexpected.Count -gt 0) {
            throw "Gate 4 artifact directory exists but does not contain the accepted package: $ArtifactDirectory"
        }
    }
    else {
        New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
    }

    & $Gh.Source run download $AcceptedRunId `
        -R CroakyJake12/CakeOS `
        -n $AcceptedArtifactName `
        -D $ArtifactDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub Actions artifact download failed for run $AcceptedRunId."
    }
}

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "Accepted package was not present after artifact download: $PackagePath"
}

$SourceHash = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($SourceHash -ne $AcceptedSha256) {
    throw "Downloaded package SHA-256 mismatch: $SourceHash"
}

$TransferPackage = Join-Path $SharePath $AcceptedPackageName
if (Test-Path -LiteralPath $TransferPackage -PathType Leaf) {
    $ExistingHash = (Get-FileHash -LiteralPath $TransferPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($ExistingHash -ne $AcceptedSha256) {
        throw "Transfer destination collision at $TransferPackage; existing file has SHA-256 $ExistingHash."
    }
}
else {
    Copy-Item -LiteralPath $PackagePath -Destination $TransferPackage
}

$TransferHash = (Get-FileHash -LiteralPath $TransferPackage -Algorithm SHA256).Hash.ToLowerInvariant()
if ($TransferHash -ne $AcceptedSha256) {
    throw "Transferred package SHA-256 mismatch: $TransferHash"
}

$Status['TransferPackagePath'] = $TransferPackage
$Status['TransferSha256'] = $TransferHash
$Status['Result'] = 'accepted-artifact-staged-without-vm-configuration-change'
[pscustomobject] $Status
