#!/usr/bin/env pwsh
<#
.SYNOPSIS
Worker 4 Continuous Release Admission - Admit a component to the release graph

.DESCRIPTION
Validates a component's release metadata against the schema, verifies REAL evidence,
and admits to the convergence matrix and dependency DAG. Does NOT build ISO.
#>

$ErrorActionPreference = 'Stop'

# Parameters (PowerShell 5.1 compatible)
$ComponentJsonPath = ""
$ReleaseDir = "release"
$DryRun = $false

# Parse arguments manually for PS 5.1
if ($args.Count -ge 1) { $ComponentJsonPath = $args[0] }
if ($args.Count -ge 2) { $ReleaseDir = $args[1] }
if ($args -contains "-DryRun") { $DryRun = $true }

if (-not $ComponentJsonPath) {
    Write-Host "Usage: admit-component.ps1 <ComponentJsonPath> [ReleaseDir] [-DryRun]" -ForegroundColor Red
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$releaseDir = Join-Path $scriptDir ".."
$fixturesPath = Join-Path $releaseDir "fixtures"
$schemaPath = Join-Path $releaseDir "schema"
$manifestPath = Join-Path $fixturesPath "release-manifest.json"
$matrixPath = Join-Path $fixturesPath "convergence-matrix.json"
$admissionLogPath = Join-Path $fixturesPath "admission-log.json"

Write-Host "=== Worker 4: Continuous Release Admission ===" -ForegroundColor Cyan
Write-Host "Component: $ComponentJsonPath" -ForegroundColor Yellow

# Load component metadata
if (-not (Test-Path -LiteralPath $ComponentJsonPath)) {
    throw "Component JSON not found: $ComponentJsonPath"
}
$componentJson = Get-Content -LiteralPath $ComponentJsonPath -Raw
$component = $componentJson | ConvertFrom-Json

# ============================================================
# VALIDATION CHECKLIST (from Worker 4 mandate)
# ============================================================

Write-Host "`n[1/10] Validating REQUIRED FIELDS..." -ForegroundColor Yellow
$requiredFields = @(
    "productId", "productType", "registryIdentity", "sourceRepository",
    "donor", "package", "entrypoint", "service", "routes", "capabilities",
    "dependencies", "persistence", "permissions", "providers",
    "offlineRequirement", "smokeTest", "provenanceState",
    "imageInclusionState", "approvedVmState"
)
foreach ($field in $requiredFields) {
    if (-not $component.psobject.Properties[$field]) {
        throw "MISSING REQUIRED FIELD: $field"
    }
}
Write-Host "  All required fields present" -ForegroundColor Green

Write-Host "`n[2/10] Validating PRODUCT ID format..." -ForegroundColor Yellow
if (-not $component.productId -or $component.productId -notmatch '^[a-zA-Z0-9._:-]{1,128}$') {
    throw "INVALID productId: '$($component.productId)'"
}
Write-Host "  Product ID: $($component.productId)" -ForegroundColor Green

Write-Host "`n[3/10] Validating PRODUCT TYPE..." -ForegroundColor Yellow
$validTypes = @('App', 'SystemSurface', 'BackgroundComponent', 'Provider', 'Service')
if ($validTypes -notcontains $component.productType) {
    throw "INVALID productType: '$($component.productType)' - must be one of: $($validTypes -join ', ')"
}
Write-Host "  Product Type: $($component.productType)" -ForegroundColor Green

Write-Host "`n[4/10] Validating SOURCE SHA..." -ForegroundColor Yellow
$src = $component.sourceRepository
if (-not $src.url -or -not $src.revision -or $src.revision -notmatch '^[a-fA-F0-9]{40}$') {
    throw "INVALID sourceRepository: url='$($src.url)' revision='$($src.revision)'"
}
Write-Host "  Source: $($src.url) @ $($src.revision)" -ForegroundColor Green

Write-Host "`n[5/10] Validating DONOR + DONOR SHA + LICENCE..." -ForegroundColor Yellow
$donor = $component.donor
if (-not $donor.name -or -not $donor.repositoryUrl -or -not $donor.revision -or -not $donor.licence) {
    throw "INVALID donor: name='$($donor.name)' repo='$($donor.repositoryUrl)' rev='$($donor.revision)' licence='$($donor.licence)'"
}
Write-Host "  Donor: $($donor.name) @ $($donor.revision) [$($donor.licence)]" -ForegroundColor Green

Write-Host "`n[6/10] Validating PACKAGE + PACKAGE HASH..." -ForegroundColor Yellow
$pkg = $component.package
if (-not $pkg.name -or -not $pkg.version -or -not $pkg.hashSha256 -or $pkg.hashSha256.Length -ne 64) {
    throw "INVALID package: name='$($pkg.name)' version='$($pkg.version)' hash='$($pkg.hashSha256)'"
}
Write-Host "  Package: $($pkg.name) $($pkg.version) [$($pkg.hashSha256)]" -ForegroundColor Green

Write-Host "`n[7/10] Validating ENTRYPOINT + ROUTES + SERVICE..." -ForegroundColor Yellow
$ep = $component.entrypoint
if (-not $ep.binaryPath) { throw "MISSING entrypoint.binaryPath" }
Write-Host "  Entrypoint: $($ep.binaryPath)" -ForegroundColor Green

if ($component.routes) {
    foreach ($route in $component.routes) {
        if (-not $route.route -or -not $route.route.StartsWith("/")) { throw "INVALID route: $($route.route)" }
        if (-not $route.handler) { throw "MISSING route handler for $($route.route)" }
    }
    Write-Host "  Routes: $($component.routes.Count)" -ForegroundColor Green
}

$svc = $component.service
Write-Host "  Service: $($svc.serviceName) Socket: $($svc.socketName) DBus: $($svc.dbusName)" -ForegroundColor Green

Write-Host "`n[8/10] Validating DEPENDENCIES + PERSISTENCE + PERMISSIONS + PROVIDERS..." -ForegroundColor Yellow
$depCount = ($component.dependencies | Measure-Object).Count
$provCount = ($component.providers | Measure-Object).Count
Write-Host "  Dependencies: $depCount" -ForegroundColor Green
Write-Host "  Persistence: Settings=$($component.persistence.requiresSettings) Cache=$($component.persistence.requiresCache) State=$($component.persistence.requiresState) Data=$($component.persistence.requiresData)" -ForegroundColor Green
Write-Host "  Permissions: Required=$($component.permissions.requiredGrants.Count) Optional=$($component.permissions.optionalGrants.Count)" -ForegroundColor Green
Write-Host "  Providers: $provCount" -ForegroundColor Green

Write-Host "`n[9/10] Validating FUNCTIONAL TESTS (SMOKE EVIDENCE)..." -ForegroundColor Yellow
$smokeEvidence = $component.smokeEvidence
if (-not $smokeEvidence) {
    throw "NO SMOKE EVIDENCE - READY requires REAL executed smoke test (executed=true, exitCode=0)"
}
if (-not $smokeEvidence.executed) {
    throw "SMOKE TEST NOT EXECUTED - executed=false"
}
if ($smokeEvidence.exitCode -ne 0) {
    throw "SMOKE TEST FAILED - exitCode=$($smokeEvidence.exitCode)"
}
Write-Host "  Smoke: executed=$($smokeEvidence.executed) exitCode=$($smokeEvidence.exitCode) at $($smokeEvidence.executedAt)" -ForegroundColor Green

Write-Host "`n[10/10] Validating APPROVED VM STATE + IMAGE STATE..." -ForegroundColor Yellow
$validProvenance = @('Unknown', 'Partial', 'Blocked', 'Ready')
$validImage = @('NotApplicable', 'Excluded', 'Pending', 'Included')
$validVm = @('NotTested', 'Failed', 'Partial', 'Verified')

if ($validProvenance -notcontains $component.provenanceState) {
    throw "INVALID provenanceState: $($component.provenanceState)"
}
if ($validImage -notcontains $component.imageInclusionState) {
    throw "INVALID imageInclusionState: $($component.imageInclusionState)"
}
if ($validVm -notcontains $component.approvedVmState) {
    throw "INVALID approvedVmState: $($component.approvedVmState)"
}

# CRITICAL: READY requires ALL evidence, not just source presence
if ($component.provenanceState -eq 'Ready') {
    if (-not $smokeEvidence -or -not $smokeEvidence.executed -or $smokeEvidence.exitCode -ne 0) {
        throw "PROVENANCE READY REQUIRES: smokeEvidence.executed=true AND exitCode=0 (got executed=$($smokeEvidence.executed) exitCode=$($smokeEvidence.exitCode))"
    }
    if ($component.approvedVmState -ne 'Verified') {
        throw "PROVENANCE READY REQUIRES: approvedVmState=Verified (got $($component.approvedVmState))"
    }
}
Write-Host "  Provenance: $($component.provenanceState)" -ForegroundColor Green
Write-Host "  Image: $($component.imageInclusionState)" -ForegroundColor Green
Write-Host "  VM State: $($component.approvedVmState)" -ForegroundColor Green

# Registry identity validation
Write-Host "`n[Registry] Validating Worker 3 registry reference..." -ForegroundColor Yellow
$reg = $component.registryIdentity
if ($reg.registryId -ne 'cakeos.shared-app-registry' -or $reg.registrySchemaVersion -ne 1) {
    throw "INVALID REGISTRY: must be cakeos.shared-app-registry v1 (got $($reg.registryId) v$($reg.registrySchemaVersion))"
}
Write-Host "  Registry: cakeos.shared-app-registry v1" -ForegroundColor Green

# ============================================================
# ADMISSION LOGIC
# ============================================================

Write-Host "`n=== ADMISSION DECISION ===" -ForegroundColor Cyan

# Load existing manifest
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$existingComponents = $manifest.components

# Check for duplicate productId
$existing = $existingComponents | Where-Object { $_.productId -eq $component.productId }
if ($existing) {
    Write-Host "  UPDATING existing component: $($component.productId)" -ForegroundColor Yellow
    $manifest.components = $existingComponents | Where-Object { $_.productId -ne $component.productId }
} else {
    Write-Host "  ADMITTING new component: $($component.productId)" -ForegroundColor Green
}

# Add component to manifest
$manifest.components += $component

# Update manifest metadata
$manifest.version = "0.1.$([int]((Get-Date -UFormat %s) % 1000))"
$manifest.createdAt = (Get-Date).ToUniversalTime().ToString("o")

if (-not $DryRun) {
    # Save updated manifest
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Host "  Manifest updated: $manifestPath" -ForegroundColor Green
}

# ============================================================
# REGENERATE CONVERGENCE MATRIX
# ============================================================

Write-Host "`n=== REGENERATING CONVERGENCE MATRIX ===" -ForegroundColor Cyan

$productStates = @()
foreach ($c in $manifest.components) {
    $smokePassed = $false
    if ($c.smokeEvidence -and $c.smokeEvidence.executed -and $c.smokeEvidence.exitCode -eq 0) {
        $smokePassed = $true
    }
    $offlineReq = $c.offlineRequirement
    $offlineMet = ($offlineReq -eq 'NotRequired') -or $smokePassed

    $isConverged = ($c.provenanceState -eq 'Ready') -and
                   ($c.imageInclusionState -eq 'Included') -and
                   ($c.approvedVmState -eq 'Verified') -and
                   $offlineMet -and
                   $smokePassed

    $blockingReason = $null
    if ($c.provenanceState -eq 'Blocked') { $blockingReason = "Provenance incomplete" }
    elseif ($c.imageInclusionState -eq 'Pending') { $blockingReason = "Image inclusion pending" }
    elseif ($c.approvedVmState -eq 'Failed') { $blockingReason = "VM validation failed" }
    elseif ($c.approvedVmState -eq 'NotTested') { $blockingReason = "VM not tested" }
    elseif (-not $smokePassed) { $blockingReason = "Smoke test not passed" }
    elseif (-not $offlineMet) { $blockingReason = "Offline requirement not met" }

    $state = @{
        productId = $c.productId
        productType = $c.productType
        provenanceState = $c.provenanceState
        imageInclusionState = $c.imageInclusionState
        approvedVmState = $c.approvedVmState
        offlineRequirementMet = $offlineMet
        smokeTestPassed = $smokePassed
        blockingReason = $blockingReason
        isConverged = $isConverged
    }
    $productStates += $state

    $convergedIcon = "+"
    $lineColor = "Green"
    if (-not $isConverged) { $convergedIcon = "-"; $lineColor = "Yellow" }
    Write-Host "  $convergedIcon $($c.productId) ($($c.productType)): Prov=$($c.provenanceState) Img=$($c.imageInclusionState) VM=$($c.approvedVmState) Smoke=$smokePassed Offline=$offlineMet" -ForegroundColor $lineColor
}

$totalProducts = $productStates.Count
$convergedProducts = 0
foreach ($ps in $productStates) { if ($ps.isConverged) { $convergedProducts++ } }
$partialProducts = 0
foreach ($ps in $productStates) { if ($ps.provenanceState -eq 'Partial' -or $ps.imageInclusionState -eq 'Pending') { $partialProducts++ } }
$blockedProducts = 0
foreach ($ps in $productStates) { if ($ps.provenanceState -eq 'Blocked' -or $ps.approvedVmState -eq 'Failed') { $blockedProducts++ } }
$unknownProducts = 0
foreach ($ps in $productStates) { if ($ps.provenanceState -eq 'Unknown') { $unknownProducts++ } }
$convergencePercentage = if ($totalProducts -gt 0) { [math]::Round(($convergedProducts / $totalProducts) * 100, 1) } else { 0 }

$matrix = @{
    releaseId = $manifest.releaseId
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    productStates = $productStates
    totalProducts = $totalProducts
    convergedProducts = $convergedProducts
    partialProducts = $partialProducts
    blockedProducts = $blockedProducts
    unknownProducts = $unknownProducts
    convergencePercentage = $convergencePercentage
}

Write-Host "`n  Total: $totalProducts | Converged: $convergedProducts | Partial: $partialProducts | Blocked: $blockedProducts | Unknown: $unknownProducts" -ForegroundColor Cyan
$pctColor = if ($convergencePercentage -ge 80) { "Green" } elseif ($convergencePercentage -ge 50) { "Yellow" } else { "Red" }
Write-Host "  Convergence: $convergencePercentage%" -ForegroundColor $pctColor

if (-not $DryRun) {
    $matrix | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $matrixPath -Encoding utf8
    Write-Host "  Matrix updated: $matrixPath" -ForegroundColor Green
}

# ============================================================
# UPDATE DEPENDENCY DAG
# ============================================================

Write-Host "`n=== UPDATING DEPENDENCY DAG ===" -ForegroundColor Cyan

$dagPath = Join-Path $fixturesPath "dependency-dag.json"
$dag = @{
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    nodes = @{}
    edges = @()
}

foreach ($c in $manifest.components) {
    $dag.nodes[$c.productId] = @{
        productId = $c.productId
        productType = $c.productType
        provenanceState = $c.provenanceState
        imageInclusionState = $c.imageInclusionState
    }

    foreach ($dep in $c.dependencies) {
        $dag.edges += @{
            from = $c.productId
            to = $dep.dependencyId
            kind = $dep.kind
            versionConstraint = $dep.versionConstraint
        }
    }
}

if (-not $DryRun) {
    $dag | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $dagPath -Encoding utf8
    Write-Host "  DAG updated: $dagPath" -ForegroundColor Green
}

$nodeCount = $dag.nodes.Count
Write-Host "  Nodes: $nodeCount | Edges: $($dag.edges.Count)" -ForegroundColor Green

# Print DAG for verification
Write-Host "`nDependency DAG:" -ForegroundColor Yellow
foreach ($edge in $dag.edges) {
    Write-Host "  $($edge.from) --($($edge.kind))--> $($edge.to)"
}

# ============================================================
# UPDATE ADMISSION LOG
# ============================================================

Write-Host "`n=== UPDATING ADMISSION LOG ===" -ForegroundColor Cyan

$targetState = $productStates | Where-Object { $_.productId -eq $component.productId } | Select-Object -First 1
$convStatus = $false
if ($targetState) { $convStatus = $targetState.isConverged }

$logEntry = @{
    timestamp = (Get-Date).ToUniversalTime().ToString("o")
    productId = $component.productId
    productType = $component.productType
    sourceRevision = $component.sourceRepository.revision
    packageHash = $component.package.hashSha256
    provenanceState = $component.provenanceState
    imageInclusionState = $component.imageInclusionState
    approvedVmState = $component.approvedVmState
    smokeExecuted = $smokeEvidence.executed
    smokeExitCode = $smokeEvidence.exitCode
    convergenceStatus = $convStatus
}

$admissionLog = @()
if (Test-Path -LiteralPath $admissionLogPath) {
    $admissionLog = Get-Content -LiteralPath $admissionLogPath -Raw | ConvertFrom-Json
}
$admissionLog += $logEntry

if (-not $DryRun) {
    $admissionLog | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $admissionLogPath -Encoding utf8
    Write-Host "  Log updated: $admissionLogPath" -ForegroundColor Green
}

# ============================================================
# SUMMARY
# ============================================================

Write-Host "`n=== ADMISSION COMPLETE ===" -ForegroundColor Cyan
Write-Host "Product: $($component.productId) ($($component.productType))" -ForegroundColor Green
Write-Host "Provenance: $($component.provenanceState) | Image: $($component.imageInclusionState) | VM: $($component.approvedVmState)" -ForegroundColor Green
Write-Host "Smoke: executed=$($smokeEvidence.executed) exitCode=$($smokeEvidence.exitCode)" -ForegroundColor Green
$finalState = $productStates | Where-Object { $_.productId -eq $component.productId } | Select-Object -First 1
$finalConv = $false
if ($finalState) { $finalConv = $finalState.isConverged }
Write-Host "Converged: $finalConv" -ForegroundColor Green
Write-Host "Overall Convergence: $convergencePercentage% ($convergedProducts/$totalProducts)" -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "`n[DRY RUN] No files were modified." -ForegroundColor Yellow
}

exit 0