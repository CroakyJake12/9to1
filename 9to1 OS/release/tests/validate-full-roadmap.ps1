#!/usr/bin/env pwsh

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$matrixPath = Join-Path $root 'fixtures/full-roadmap-convergence-matrix.json'
$matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json

if ($matrix.roadmapScope -ne 'FullRoadmap') {
    throw "Expected FullRoadmap scope, got '$($matrix.roadmapScope)'."
}

$requiredDefaults = @(
    'sourceSha', 'sourceIntegrationState', 'donorRevision', 'license', 'package',
    'packageSha256', 'entrypoint', 'routes', 'dependencies', 'persistence',
    'permissions', 'providers', 'tests', 'installedPathSmoke',
    'approvedVmAcceptance', 'imageInclusionEvidence', 'provenanceEvidence'
)
foreach ($field in $requiredDefaults) {
    if (-not $matrix.evidenceDefaults.PSObject.Properties.Name.Contains($field)) {
        throw "Missing explicit evidence default '$field'."
    }
}

$stateIds = @($matrix.productStates | ForEach-Object { $_.productId })
if ($stateIds.Count -ne $matrix.totalProducts) {
    throw "totalProducts ($($matrix.totalProducts)) does not match state count ($($stateIds.Count))."
}
if ((@($stateIds | Select-Object -Unique)).Count -ne $stateIds.Count) {
    throw 'Full-roadmap matrix contains duplicate product IDs.'
}

$requiredIds = @($matrix.requiredProductIds)
if ((@($requiredIds | Select-Object -Unique)).Count -ne $requiredIds.Count) {
    throw 'Full-roadmap requirement list contains duplicate product IDs.'
}
if ($requiredIds.Count -ne $matrix.totalProducts) {
    throw "Requirement count ($($requiredIds.Count)) does not match totalProducts ($($matrix.totalProducts))."
}

$missing = @($requiredIds | Where-Object { $_ -notin $stateIds })
$unexpected = @($stateIds | Where-Object { $_ -notin $requiredIds })
if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
    throw "Roadmap coverage mismatch. Missing: $($missing -join ', '); unexpected: $($unexpected -join ', ')."
}

$validRoadmapStates = @('NOT_STARTED', 'IMPLEMENTING', 'PARTIAL', 'READY', 'BLOCKED')
foreach ($state in $matrix.productStates) {
    if ($state.roadmapState -notin $validRoadmapStates) {
        throw "$($state.productId): invalid roadmap state '$($state.roadmapState)'."
    }
    if (-not $state.PSObject.Properties.Name.Contains('sourceSha') -and -not $matrix.evidenceDefaults.sourceSha) {
        throw "$($state.productId): source SHA is neither explicit nor inherited."
    }
    if ($state.isConverged -and ($state.roadmapState -ne 'READY' -or $state.provenanceState -ne 'Ready' -or $state.imageInclusionState -ne 'Included' -or $state.approvedVmState -ne 'Verified' -or -not $state.offlineRequirementMet -or -not $state.smokeTestPassed)) {
        throw "$($state.productId): converged state lacks complete RC evidence."
    }
}

function Get-StateCount([string] $state) {
    return @($matrix.productStates | Where-Object { $_.roadmapState -eq $state }).Count
}

$expectedCounts = @{
    NOT_STARTED = [int]$matrix.notStartedProducts
    IMPLEMENTING = [int]$matrix.implementingProducts
    PARTIAL = [int]$matrix.partialProducts
    READY = [int]$matrix.readyProducts
    BLOCKED = [int]$matrix.blockedProducts
}
foreach ($state in $expectedCounts.Keys) {
    $actual = Get-StateCount $state
    if ($actual -ne $expectedCounts[$state]) {
        throw "State count for $state is $actual, expected $($expectedCounts[$state])."
    }
}

$converged = @($matrix.productStates | Where-Object { $_.isConverged }).Count
if ($converged -ne $matrix.convergedProducts) {
    throw "convergedProducts ($($matrix.convergedProducts)) does not match converged state count ($converged)."
}

Write-Host "Full roadmap validation passed: $($matrix.totalProducts) required products, $($matrix.convergedProducts) converged." -ForegroundColor Green
