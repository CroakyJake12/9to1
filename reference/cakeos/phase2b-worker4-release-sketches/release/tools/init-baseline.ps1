#!/usr/bin/env pwsh
<#
.SYNOPSIS
Worker 4 Phase 2B - Initialize admission from baseline fixtures

.DESCRIPTION
Sets up the admission log, dependency DAG, and convergence matrix from the
existing validated fixtures. This establishes the baseline state.
#>

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$releaseDir = Join-Path $scriptDir ".."
$fixturesPath = Join-Path $releaseDir "fixtures"
$manifestPath = Join-Path $fixturesPath "release-manifest.json"
$matrixPath = Join-Path $fixturesPath "convergence-matrix.json"
$dagPath = Join-Path $fixturesPath "dependency-dag.json"
$admissionLogPath = Join-Path $fixturesPath "admission-log.json"

Write-Host "=== Worker 4 Phase 2B: Initialize Admission Baseline ===" -ForegroundColor Cyan
Write-Host "Fixtures: $fixturesPath" -ForegroundColor Gray

# Load manifest
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json

Write-Host "Loaded manifest: $($manifest.releaseId) v$($manifest.version)" -ForegroundColor Green
Write-Host "Components: $($manifest.components.Count)" -ForegroundColor Green

# Initialize admission log from existing matrix states
$admissionLog = @()
foreach ($c in $manifest.components) {
    $state = $matrix.productStates | Where-Object { $_.productId -eq $c.productId } | Select-Object -First 1
    
    $ts = (Get-Date).ToUniversalTime().ToString("o")
    if ($c.smokeEvidence -and $c.smokeEvidence.executedAt) { $ts = $c.smokeEvidence.executedAt }
    $smokeExec = $false
    $smokeCode = -1
    if ($c.smokeEvidence) {
        $smokeExec = $c.smokeEvidence.executed
        $smokeCode = $c.smokeEvidence.exitCode
    }
    $convStatus = $false
    if ($state) { $convStatus = $state.isConverged }

    $logEntry = @{
        timestamp = $ts
        productId = $c.productId
        productType = $c.productType
        sourceRevision = $c.sourceRepository.revision
        packageHash = $c.package.hashSha256
        provenanceState = $c.provenanceState
        imageInclusionState = $c.imageInclusionState
        approvedVmState = $c.approvedVmState
        smokeExecuted = $smokeExec
        smokeExitCode = $smokeCode
        convergenceStatus = $convStatus
    }
    $admissionLog += $logEntry
}

$admissionLog | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $admissionLogPath -Encoding utf8
Write-Host "Admission log initialized: $admissionLogPath ($($admissionLog.Count) entries)" -ForegroundColor Green

# Regenerate dependency DAG
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

$dag | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $dagPath -Encoding utf8
Write-Host "Dependency DAG initialized: $dagPath ($($dag.nodes.Count) nodes, $($dag.edges.Count) edges)" -ForegroundColor Green

# Verify convergence matrix is consistent
Write-Host "`nVerifying convergence matrix consistency..." -ForegroundColor Yellow
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
}

# Verify against stored matrix
$allMatch = $true
foreach ($stored in $matrix.productStates) {
    $computed = $productStates | Where-Object { $_.productId -eq $stored.productId } | Select-Object -First 1
    if (-not $computed) {
        Write-Host "  MISSING in computed: $($stored.productId)" -ForegroundColor Red
        $allMatch = $false
        continue
    }
    if ($computed.isConverged -ne $stored.isConverged) {
        Write-Host "  MISMATCH $($stored.productId): computed=$($computed.isConverged) stored=$($stored.isConverged)" -ForegroundColor Red
        $allMatch = $false
    }
    if ($computed.smokeTestPassed -ne $stored.smokeTestPassed) {
        Write-Host "  SMOKE MISMATCH $($stored.productId): computed=$($computed.smokeTestPassed) stored=$($stored.smokeTestPassed)" -ForegroundColor Red
        $allMatch = $false
    }
}

if ($allMatch) {
    Write-Host "  Convergence matrix VERIFIED - all states consistent" -ForegroundColor Green
} else {
    Write-Host "  Convergence matrix INCONSISTENT - regenerating" -ForegroundColor Yellow
    $totalProducts = $productStates.Count
    $convergedProducts = ($productStates | Where-Object { $_.isConverged }).Count
    $partialProducts = ($productStates | Where-Object { $_.provenanceState -eq 'Partial' -or $_.imageInclusionState -eq 'Pending' }).Count
    $blockedProducts = ($productStates | Where-Object { $_.provenanceState -eq 'Blocked' -or $_.approvedVmState -eq 'Failed' }).Count
    $unknownProducts = ($productStates | Where-Object { $_.provenanceState -eq 'Unknown' }).Count
    $convergencePercentage = if ($totalProducts -gt 0) { [math]::Round(($convergedProducts / $totalProducts) * 100, 1) } else { 0 }

    $newMatrix = @{
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
    $newMatrix | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $matrixPath -Encoding utf8
    Write-Host "  Matrix regenerated and saved" -ForegroundColor Green
}

Write-Host "`n=== BASELINE INITIALIZATION COMPLETE ===" -ForegroundColor Cyan
Write-Host "Manifest: $manifestPath" -ForegroundColor Gray
Write-Host "Matrix: $matrixPath" -ForegroundColor Gray
Write-Host "DAG: $dagPath" -ForegroundColor Gray
Write-Host "Log: $admissionLogPath" -ForegroundColor Gray

exit 0