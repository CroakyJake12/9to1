[CmdletBinding()]
param(
    [string] $ManifestPath,
    [string] $PackageDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $root 'platform\ubuntu\release-staging.json'
}
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $root 'artifacts\packages'
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Release staging manifest is missing: $ManifestPath"
}
if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
    throw "Release staging directory is missing: $PackageDirectory"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw 'Unsupported release staging manifest schema.'
}

$ready = @($manifest.cohort | Where-Object { $_.state -eq 'READY' })
$blocked = @($manifest.cohort | Where-Object { $_.state -in @('PARTIAL', 'BLOCKED', 'UNKNOWN') })
foreach ($record in $blocked) {
    if ($record.stage) {
        throw "$($record.component) is not eligible for staging: state=$($record.state), stage=$($record.stage)"
    }
}
foreach ($record in $ready) {
    foreach ($field in 'repository', 'workflow', 'artifactId', 'digest', 'debFile', 'sha256', 'architecture', 'dependencies', 'launcher', 'installSmoke', 'runtimeSmoke', 'stagePath') {
        if ([string]::IsNullOrWhiteSpace([string]$record.$field) -or [string]$record.$field -eq 'UNKNOWN') {
            throw "$($record.component) is missing immutable staging metadata: $field"
        }
    }
    $path = Join-Path $PackageDirectory $record.debFile
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required READY cohort artifact is missing: $($record.debFile)"
    }
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $record.sha256.ToLowerInvariant()) {
        throw "$($record.component) artifact hash mismatch: expected $($record.sha256), got $actualHash"
    }
    if ($record.digest.ToLowerInvariant() -ne $actualHash) {
        throw "$($record.component) CI digest does not match package SHA-256."
    }
    Write-Output "Release staging record passed: $($record.component) / $($record.artifactId)"
}

if ($ready.Count -eq 0) {
    throw 'Release staging manifest contains no READY cohort records.'
}
