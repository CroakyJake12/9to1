#!/usr/bin/env powershell
[CmdletBinding()]
param(
    [string] $PreloadManifestPath,
    [string] $ReleaseManifestPath,
    [string] $ArtifactLockPath,
    [string] $StagingManifestPath,
    [string] $PackageDirectory,
    [switch] $RequireArtifacts
)

$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $releaseRoot

if ([string]::IsNullOrWhiteSpace($PreloadManifestPath)) {
    $PreloadManifestPath = Join-Path $releaseRoot 'package-preload-manifest.json'
}
if ([string]::IsNullOrWhiteSpace($ReleaseManifestPath)) {
    $ReleaseManifestPath = Join-Path $releaseRoot 'emergency-preview-0.1.json'
}
if ([string]::IsNullOrWhiteSpace($ArtifactLockPath)) {
    $ArtifactLockPath = Join-Path $repoRoot 'packaging\llamacpp\cohort-artifact.lock.json'
}
if ([string]::IsNullOrWhiteSpace($StagingManifestPath)) {
    $StagingManifestPath = Join-Path $repoRoot 'platform\ubuntu\release-staging.json'
}
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot 'artifacts\packages'
}

function Require-Value {
    param([object] $Value, [string] $Message)
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string] $Value)) {
        throw $Message
    }
}

function Require-File {
    param([string] $Path, [string] $Description)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
}

function Require-SafeRelativePath {
    param([string] $Value, [string] $Context)
    Require-Value $Value "$Context is missing."
    if ([System.IO.Path]::IsPathRooted($Value) -or $Value -match '^[A-Za-z]:' -or $Value -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "$Context must be a repository-relative path."
    }
}

function Require-SameValues {
    param([object[]] $Expected, [object[]] $Actual, [string] $Context)
    $difference = @(Compare-Object -ReferenceObject @($Expected) -DifferenceObject @($Actual))
    if ($difference.Count -ne 0) {
        throw "$Context does not match the immutable metadata."
    }
}

foreach ($input in @(
    @{ Path = $PreloadManifestPath; Description = 'Package preload manifest' },
    @{ Path = $ReleaseManifestPath; Description = 'Emergency release manifest' },
    @{ Path = $ArtifactLockPath; Description = 'Immutable artifact lock' },
    @{ Path = $StagingManifestPath; Description = 'Release staging manifest' }
)) {
    Require-File $input.Path $input.Description
}

$preload = Get-Content -LiteralPath $PreloadManifestPath -Raw | ConvertFrom-Json
$release = Get-Content -LiteralPath $ReleaseManifestPath -Raw | ConvertFrom-Json
$lock = Get-Content -LiteralPath $ArtifactLockPath -Raw | ConvertFrom-Json
$staging = Get-Content -LiteralPath $StagingManifestPath -Raw | ConvertFrom-Json

if ($preload.schemaVersion -ne 1 -or $preload.releaseId -ne 'cakeos-emergency-preview-0.1' -or $preload.version -ne '0.1.0') {
    throw 'Unsupported or unexpected package preload manifest identity.'
}
if ($preload.releaseState -ne 'BLOCKED' -or $release.releaseState -ne 'BLOCKED') {
    throw 'The preload model and release must remain blocked until artifact and image gates are satisfied.'
}
if ($release.packagePreloadManifest -ne 'release/package-preload-manifest.json') {
    throw 'Emergency release does not reference the package preload manifest.'
}
if ($preload.artifactAvailability -ne 'external-staging-required') {
    throw 'The preload manifest must require externally staged package artifacts.'
}

$policy = $preload.preload
if ($policy.mode -ne 'copy-only' -or $policy.networkAllowed -or $policy.buildAllowed -or $policy.installAllowed -or $policy.vmMutationAllowed) {
    throw 'Package preload must remain a local copy-only operation.'
}
if ($policy.licensePolicy -ne 'preserve-artifact-bytes') {
    throw 'Package preload must preserve artifact bytes and embedded license material.'
}
foreach ($pathField in 'sourceDirectory', 'outputDirectory') {
    Require-SafeRelativePath ([string] $policy.$pathField) "preload.$pathField"
}
if ($policy.sourceDirectory -ne $release.cohortAssembly.inputDirectory -or $policy.outputDirectory -ne $release.cohortAssembly.outputDirectory) {
    throw 'Package preload directories do not match the release cohort assembly.'
}

$imageBuilder = $preload.imageBuilder
if ($imageBuilder.packageDirectoryEnvironment -ne 'HAVENOS_DEB_DIR' -or $imageBuilder.state -ne 'BLOCKED') {
    throw 'Package preload does not use the supported blocked image-builder handoff.'
}
foreach ($pathField in 'script', 'destination') {
    Require-SafeRelativePath ([string] $imageBuilder.$pathField) "imageBuilder.$pathField"
}
Require-File (Join-Path $repoRoot $imageBuilder.script) 'Image builder'
$imagePackageParent = Split-Path -Parent (Join-Path $repoRoot $imageBuilder.destination)
if (-not (Test-Path -LiteralPath $imagePackageParent -PathType Container)) {
    throw "Image package destination parent is missing: $imagePackageParent"
}

$packages = @($preload.packages)
$releaseCandidates = @($release.candidatePackageGraph | Where-Object { $_.state -eq 'CANDIDATE' })
if ($packages.Count -eq 0 -or $packages.Count -ne $releaseCandidates.Count) {
    throw 'Package preload entries do not match the release candidate graph.'
}
Require-SameValues @($releaseCandidates | ForEach-Object { $_.id }) @($packages | ForEach-Object { $_.id }) 'Package preload identifiers'
$orders = @($packages | ForEach-Object { $_.assemblyOrder })
if (@($orders | Select-Object -Unique).Count -ne $orders.Count) {
    throw 'Package preload assembly order must be unique.'
}

foreach ($package in $packages) {
    foreach ($field in 'id', 'packageName', 'fileName', 'sha256', 'architecture', 'entrypoint', 'assemblyOrder') {
        Require-Value $package.$field "$($package.id) is missing $field."
    }
    if ($package.sourceType -ne 'standalone-debian-artifact') {
        throw "$($package.id) must reference a standalone Debian artifact."
    }
    if ($package.licensePolicy -ne $policy.licensePolicy) {
        throw "$($package.id) does not preserve its artifact bytes and embedded license material."
    }
    if ($package.fileName -notmatch '^[^\\/]+\.deb$') {
        throw "$($package.id) package filename must be a bare .deb filename."
    }
    if ($package.sha256 -notmatch '^[a-f0-9]{64}$') {
        throw "$($package.id) has an invalid SHA-256."
    }
    if ($package.architecture -ne 'amd64' -or $package.entrypoint -notmatch '^/usr/') {
        throw "$($package.id) has invalid architecture or installed entrypoint metadata."
    }
    if (@($package.dependencies).Count -eq 0 -or @($package.dependencies | Where-Object { [string]::IsNullOrWhiteSpace([string] $_) }).Count -ne 0) {
        throw "$($package.id) is missing dependency metadata."
    }
    foreach ($field in 'repository', 'ref', 'revision', 'workflowArtifactId', 'workflowArtifactName') {
        Require-Value $package.source.$field "$($package.id) is missing source.$field."
    }
    foreach ($pathField in 'immutableArtifactLock', 'releaseStagingRecord', 'releaseCandidateGraph') {
        $evidencePath = [string] $package.evidence.$pathField
        Require-SafeRelativePath $evidencePath "$($package.id) evidence.$pathField"
        Require-File (Join-Path $repoRoot $evidencePath) "$($package.id) evidence"
    }
    if ($package.evidence.immutableArtifactLock -ne 'packaging/llamacpp/cohort-artifact.lock.json' -or $package.evidence.releaseStagingRecord -ne 'platform/ubuntu/release-staging.json' -or $package.evidence.releaseCandidateGraph -ne 'release/emergency-preview-0.1.json') {
        throw "$($package.id) references unexpected evidence sources."
    }

    $candidate = @($releaseCandidates | Where-Object { $_.id -eq $package.id })
    if ($candidate.Count -ne 1) {
        throw "$($package.id) is missing its release candidate."
    }
    foreach ($field in 'assemblyOrder', 'packageName', 'fileName', 'sha256', 'architecture', 'entrypoint') {
        if ([string] $package.$field -ne [string] $candidate[0].$field) {
            throw "$($package.id) $field does not match the release candidate."
        }
    }
    Require-SameValues @($candidate[0].dependencies) @($package.dependencies) "$($package.id) dependencies"
    foreach ($field in 'repository', 'ref', 'revision', 'workflowArtifactId') {
        if ([string] $package.source.$field -ne [string] $candidate[0].source.$field) {
            throw "$($package.id) source.$field does not match the release candidate."
        }
    }

    $locked = @($lock.artifacts | Where-Object { $_.package.filename -eq $package.fileName })
    if ($locked.Count -ne 1) {
        throw "$($package.id) is missing its immutable artifact lock entry."
    }
    if ($locked[0].package.sha256 -ne $package.sha256 -or $locked[0].package.architecture -ne $package.architecture -or $locked[0].package.launcher -ne $package.entrypoint -or [string] $locked[0].workflow.artifactId -ne [string] $package.source.workflowArtifactId -or $locked[0].workflow.artifactName -ne $package.source.workflowArtifactName) {
        throw "$($package.id) does not match immutable artifact metadata."
    }
    Require-SameValues @($locked[0].package.dependencies) @($package.dependencies) "$($package.id) lock dependencies"
    foreach ($field in 'repository', 'ref', 'revision') {
        if ([string] $locked[0].source.$field -ne [string] $package.source.$field) {
            throw "$($package.id) source.$field does not match the immutable artifact lock."
        }
    }

    $staged = @($staging.cohort | Where-Object { $_.debFile -eq $package.fileName })
    if ($staged.Count -ne 1 -or $staged[0].state -ne 'READY') {
        throw "$($package.id) is missing a READY release staging record."
    }
    if ($staged[0].sha256 -ne $package.sha256 -or $staged[0].architecture -ne $package.architecture -or $staged[0].launcher -ne $package.entrypoint -or [string] $staged[0].artifactId -ne [string] $package.source.workflowArtifactId) {
        throw "$($package.id) does not match release staging metadata."
    }
    Require-SameValues @($staged[0].dependencies -split '\s*,\s*') @($package.dependencies) "$($package.id) staging dependencies"
}

if ($RequireArtifacts) {
    if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
        throw "Package directory is missing: $PackageDirectory"
    }
    foreach ($package in $packages) {
        $artifactPath = Join-Path $PackageDirectory $package.fileName
        Require-File $artifactPath "$($package.id) package artifact"
        $artifact = Get-Item -LiteralPath $artifactPath
        if (($artifact.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$($package.id) package artifact must not be a reparse point."
        }
        $actualHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $package.sha256) {
            throw "$($package.id) package artifact SHA-256 does not match the preload manifest."
        }
    }
    Write-Output 'Package preload metadata and artifact SHA-256 checks passed without Debian package tools.'
}
else {
    Write-Output 'Package preload metadata, release graph, immutable lock, staging mapping, and copy-only policy passed.'
    Write-Output 'Package bytes were not checked; rerun with -RequireArtifacts after the standalone .deb files are staged.'
}
