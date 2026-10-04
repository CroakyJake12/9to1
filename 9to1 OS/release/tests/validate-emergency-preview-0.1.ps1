#!/usr/bin/env powershell
[CmdletBinding()]
param(
    [string] $ManifestPath,
    [string] $PackageDirectory,
    [switch] $RequireArtifacts
)

$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $releaseRoot

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $releaseRoot 'emergency-preview-0.1.json'
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

function Require-RepoFiles {
    param([object[]] $Paths, [string] $Context)
    if (@($Paths).Count -eq 0) {
        throw "$Context is missing evidence files."
    }
    foreach ($relativePath in @($Paths)) {
        Require-Value $relativePath "$Context contains an empty evidence path."
        $path = Join-Path $repoRoot $relativePath
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "$Context evidence file is missing: $relativePath"
        }
    }
}

function Require-SameValues {
    param([object[]] $Expected, [object[]] $Actual, [string] $Context)
    $difference = @(Compare-Object -ReferenceObject @($Expected) -DifferenceObject @($Actual))
    if ($difference.Count -ne 0) {
        throw "$Context does not match the immutable evidence."
    }
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Emergency manifest is missing: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw 'Unsupported emergency manifest schema.'
}
if ($manifest.releaseId -ne 'cakeos-emergency-preview-0.1' -or $manifest.version -ne '0.1.0') {
    throw 'Unexpected emergency release identity.'
}
if ($manifest.preparedFromBaseRevision -ne '289c8e84f4e9bcdc6a08a281bc8a9cb98b94b31b') {
    throw 'Emergency manifest is not pinned to the required base revision.'
}
if ($manifest.releaseState -ne 'BLOCKED') {
    throw 'The emergency release must remain blocked until package and image gates are satisfied.'
}
if ($manifest.packagePreloadManifest -ne 'release/package-preload-manifest.json') {
    throw 'Emergency release must reference the package preload manifest.'
}
Require-RepoFiles @($manifest.packagePreloadManifest) 'Package preload manifest'

$criteria = $manifest.inclusionCriteria
if ($criteria.candidateState -ne 'CANDIDATE') {
    throw 'Unexpected candidate state in inclusion criteria.'
}
foreach ($field in @($criteria.requiredPackageFields) + @($criteria.requiredRuntimeFields) + @($criteria.requiredEvidence)) {
    Require-Value $field 'Inclusion criteria contains an empty field name.'
}
if (@($criteria.canvasBoardsRequiredEvidence).Count -lt 5) {
    throw 'Canvas/Boards inclusion criteria is incomplete.'
}

$liveBuild = $manifest.liveBuildInput
if ($liveBuild.state -ne 'BLOCKED') {
    throw 'Live-build must remain blocked until a verified local cohort is available.'
}
foreach ($field in 'buildScript', 'configuration', 'packageList', 'packageDirectory', 'blockingReason') {
    Require-Value $liveBuild.$field "Live-build input is missing $field."
}
Require-RepoFiles @($liveBuild.buildScript, $liveBuild.configuration, $liveBuild.packageList) 'Live-build input'

$candidates = @($manifest.candidatePackageGraph)
$requiredCandidateIds = @('haven-hui-preview', 'haven-llamacpp-runtime')
if ($candidates.Count -ne $requiredCandidateIds.Count) {
    throw 'The emergency candidate graph must contain exactly the two immutable package candidates.'
}
Require-SameValues $requiredCandidateIds @($candidates | ForEach-Object { $_.id }) 'Candidate identifiers'

$orders = @($candidates | ForEach-Object { $_.assemblyOrder })
if (@($orders | Select-Object -Unique).Count -ne $orders.Count) {
    throw 'Candidate assembly order must be unique.'
}

$lockPath = Join-Path $repoRoot 'packaging\llamacpp\cohort-artifact.lock.json'
$stagingPath = Join-Path $repoRoot 'platform\ubuntu\release-staging.json'
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$staging = Get-Content -LiteralPath $stagingPath -Raw | ConvertFrom-Json

foreach ($candidate in $candidates) {
    if ($candidate.state -ne $criteria.candidateState) {
        throw "$($candidate.id) is not a candidate."
    }
    foreach ($field in 'id', 'packageName', 'fileName', 'sha256', 'architecture', 'entrypoint') {
        Require-Value $candidate.$field "$($candidate.id) is missing $field."
    }
    foreach ($field in 'repository', 'ref', 'revision', 'workflowArtifactId') {
        Require-Value $candidate.source.$field "$($candidate.id) is missing source.$field."
    }
    if ($candidate.fileName -match '[\\/]') {
        throw "$($candidate.id) package filename must not contain a path."
    }
    if ($candidate.sha256 -notmatch '^[a-f0-9]{64}$') {
        throw "$($candidate.id) has an invalid SHA-256."
    }
    if ($candidate.architecture -ne 'amd64') {
        throw "$($candidate.id) has an unsupported architecture."
    }
    if ($candidate.entrypoint -notmatch '^/usr/') {
        throw "$($candidate.id) entrypoint must be an installed absolute path."
    }
    if (@($candidate.dependencies).Count -eq 0 -or @($candidate.dependencies | Where-Object { [string]::IsNullOrWhiteSpace([string] $_) }).Count -ne 0) {
        throw "$($candidate.id) is missing dependencies."
    }
    if (@($criteria.forbiddenPackageNames) -contains $candidate.packageName) {
        throw "$($candidate.id) is prohibited by the Worker 2 package exclusion policy."
    }
    Require-RepoFiles @($candidate.evidenceFiles) "$($candidate.id)"

    $locked = @($lock.artifacts | Where-Object { $_.package.filename -eq $candidate.fileName })
    if ($locked.Count -ne 1) {
        throw "$($candidate.id) is missing its immutable artifact lock record."
    }
    if ($locked[0].package.sha256 -ne $candidate.sha256 -or $locked[0].package.architecture -ne $candidate.architecture -or $locked[0].package.launcher -ne $candidate.entrypoint) {
        throw "$($candidate.id) does not match its immutable artifact lock metadata."
    }
    if ($locked[0].source.repository -ne $candidate.source.repository -or $locked[0].source.ref -ne $candidate.source.ref -or $locked[0].source.revision -ne $candidate.source.revision -or [string] $locked[0].workflow.artifactId -ne [string] $candidate.source.workflowArtifactId) {
        throw "$($candidate.id) does not match its immutable source artifact identity."
    }
    Require-SameValues @($locked[0].package.dependencies) @($candidate.dependencies) "$($candidate.id) dependencies"

    $staged = @($staging.cohort | Where-Object { $_.debFile -eq $candidate.fileName })
    if ($staged.Count -ne 1 -or $staged[0].state -ne 'READY') {
        throw "$($candidate.id) is missing a READY release staging record."
    }
    if ($staged[0].sha256 -ne $candidate.sha256 -or $staged[0].launcher -ne $candidate.entrypoint -or $staged[0].architecture -ne $candidate.architecture) {
        throw "$($candidate.id) does not match its release staging record."
    }
    if ([string] $staged[0].artifactId -ne [string] $candidate.source.workflowArtifactId) {
        throw "$($candidate.id) does not match its release staging artifact identity."
    }
    Require-SameValues @($staged[0].dependencies -split '\s*,\s*') @($candidate.dependencies) "$($candidate.id) staging dependencies"
}

$canvasBoards = @($manifest.canvasBoardsEvidence)
$requiredCanvasBoards = @('cakeos-canvas-rnote', 'cakeos-boards')
Require-SameValues $requiredCanvasBoards @($canvasBoards | ForEach-Object { $_.componentId }) 'Canvas/Boards evidence records'
foreach ($record in $canvasBoards) {
    if ($record.state -ne 'BLOCKED') {
        throw "$($record.componentId) must remain blocked without dedicated package evidence."
    }
    Require-RepoFiles @($record.evidenceFiles) "$($record.componentId)"
    Require-SameValues @($criteria.canvasBoardsRequiredEvidence) @($record.missingRequiredEvidence) "$($record.componentId) required evidence"
    Require-Value $record.blockingReason "$($record.componentId) is missing its blocking reason."
}

$excluded = @($manifest.excludedComponents)
foreach ($forbiddenPackage in @($criteria.forbiddenPackageNames)) {
    $record = @($excluded | Where-Object { $_.componentId -eq $forbiddenPackage })
    if ($record.Count -ne 1 -or $record[0].state -ne 'EXCLUDED') {
        throw "$forbiddenPackage must be explicitly excluded."
    }
}
foreach ($record in $excluded) {
    if ($record.state -notin @('EXCLUDED', 'BLOCKED')) {
        throw "$($record.componentId) has an invalid excluded-component state."
    }
    Require-RepoFiles @($record.evidenceFiles) "$($record.componentId)"
    if (@($record.missingAdmissionEvidence).Count -eq 0) {
        throw "$($record.componentId) is missing its admission evidence gap."
    }
    Require-Value $record.blockingReason "$($record.componentId) is missing its blocking reason."
}

$assembly = $manifest.cohortAssembly
if ($assembly.copyMode -ne 'copy-only' -or $assembly.networkAllowed -or $assembly.buildAllowed -or $assembly.installAllowed -or $assembly.vmMutationAllowed) {
    throw 'Cohort assembly must remain a local copy-only operation.'
}
foreach ($field in 'inputDirectory', 'outputDirectory', 'ordering', 'cohortManifest', 'checksums') {
    Require-Value $assembly.$field "Cohort assembly is missing $field."
}

if ($RequireArtifacts) {
    if (-not (Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
        throw "Candidate package directory is missing: $PackageDirectory. No candidate package can be admitted."
    }
    $dpkgDeb = Get-Command dpkg-deb -ErrorAction SilentlyContinue
    if ($null -eq $dpkgDeb) {
        throw 'dpkg-deb is required to validate candidate package metadata and entrypoints.'
    }
    foreach ($candidate in $candidates) {
        $packagePath = Join-Path $PackageDirectory $candidate.fileName
        if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
            throw "Candidate package is missing: $($candidate.fileName)"
        }
        $actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $candidate.sha256) {
            throw "$($candidate.id) package hash mismatch."
        }
        $packageName = (& $dpkgDeb.Path --field $packagePath Package).Trim()
        if ($LASTEXITCODE -ne 0 -or $packageName -ne $candidate.packageName) {
            throw "$($candidate.id) package metadata does not identify $($candidate.packageName)."
        }
        $depends = (& $dpkgDeb.Path --field $packagePath Depends).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "$($candidate.id) package dependency metadata could not be read."
        }
        Require-SameValues @($candidate.dependencies) @($depends -split '\s*,\s*') "$($candidate.id) package dependencies"

        $extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("cakeos-emergency-" + [System.Guid]::NewGuid().ToString('N'))
        try {
            & $dpkgDeb.Path --extract $packagePath $extractRoot
            if ($LASTEXITCODE -ne 0) {
                throw "$($candidate.id) package extraction failed."
            }
            $relativeEntrypoint = $candidate.entrypoint.TrimStart([char[]]@('/'))
            if (-not (Test-Path -LiteralPath (Join-Path $extractRoot $relativeEntrypoint) -PathType Leaf)) {
                throw "$($candidate.id) package entrypoint is missing: $($candidate.entrypoint)"
            }
        }
        finally {
            if (Test-Path -LiteralPath $extractRoot) {
                Remove-Item -LiteralPath $extractRoot -Recurse -Force
            }
        }
    }
    Write-Output 'Emergency candidate packages passed hash, metadata, dependency, and entrypoint validation.'
}
else {
    Write-Output 'Emergency manifest structure, immutable lock alignment, exclusions, and deterministic assembly policy passed.'
    Write-Output 'Package artifacts were not admitted; rerun with -RequireArtifacts on a Linux package staging host.'
}
