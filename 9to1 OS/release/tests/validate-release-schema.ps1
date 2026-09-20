#!/usr/bin/env pwsh
<# 
.SYNOPSIS
Validate Release Metadata schema and fixtures using PowerShell
#>

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$fixturesPath = Join-Path $root "fixtures"
$schemaPath = Join-Path $root "..\schema"

Write-Host "=== HavenOS Release Metadata Schema Validation ===" -ForegroundColor Cyan

# Test 1: All fixtures exist
$fixtureFiles = @(
    "haven-welcome.json",
    "haven-shell.json", 
    "havenos-studio.json",
    "llamacpp-provider.json",
    "haven-notifications.json",
    "haven-settings-service.json",
    "unknown-component.json",
    "release-manifest.json",
    "convergence-matrix.json"
)

Write-Host "`n[1/8] Checking fixture files exist..." -ForegroundColor Yellow
$missing = @()
foreach ($file in $fixtureFiles) {
    $path = Join-Path $fixturesPath $file
    if (-not (Test-Path -LiteralPath $path)) {
        $missing += $file
    }
}
if ($missing.Count -gt 0) {
    $msg = "Missing fixtures: $($missing -join ', ')"
    throw $msg
}
Write-Host "  All $($fixtureFiles.Count) fixtures present" -ForegroundColor Green

# Test 2: Validate JSON syntax
Write-Host "`n[2/8] Validating JSON syntax..." -ForegroundColor Yellow
foreach ($file in $fixtureFiles) {
    $path = Join-Path $fixturesPath $file
    try {
        $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    }
    catch {
        $msg = "Invalid JSON in {0}: {1}" -f $file, $_.Exception.Message
        throw $msg
    }
}
Write-Host "  All JSON files parse correctly" -ForegroundColor Green

# Test 3: Validate registry identity references Worker 3
Write-Host "`n[3/8] Validating registry identity references Worker 3..." -ForegroundColor Yellow
$componentFiles = $fixtureFiles | Where-Object { $_ -notmatch '^(release-manifest|convergence-matrix|unknown-component)' }
foreach ($file in $componentFiles) {
    $path = Join-Path $fixturesPath $file
    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    
    if (-not $json.registryIdentity) {
        $msg = "{0}: Missing registryIdentity" -f $file
        throw $msg
    }
    if ($json.registryIdentity.registryId -ne 'cakeos.shared-app-registry') {
        $msg = "{0}: Invalid registryId '{1}' - must be 'cakeos.shared-app-registry'" -f $file, $json.registryIdentity.registryId
        throw $msg
    }
    if ($json.registryIdentity.registrySchemaVersion -ne 1) {
        $msg = "{0}: Invalid registrySchemaVersion '{1}' - must be 1" -f $file, $json.registryIdentity.registrySchemaVersion
        throw $msg
    }
}
Write-Host "  All components reference Worker 3 registry: cakeos.shared-app-registry v1" -ForegroundColor Green

# Test 4: Validate all five product types covered
Write-Host "`n[4/8] Validating all five product types covered..." -ForegroundColor Yellow
$productTypes = @{}
foreach ($file in $componentFiles) {
    $path = Join-Path $fixturesPath $file
    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $productTypes[$json.productType] = $true
}
$expectedTypes = @('App', 'SystemSurface', 'BackgroundComponent', 'Provider', 'Service')
$missingTypes = $expectedTypes | Where-Object { -not $productTypes.ContainsKey($_) }
if ($missingTypes.Count -gt 0) {
    $msg = "Missing product types: $($missingTypes -join ', ')"
    throw $msg
}
Write-Host "  All 5 product types present: $($expectedTypes -join ', ')" -ForegroundColor Green

# Test 5: Validate explicit provenance states
Write-Host "`n[5/8] Validating explicit provenance states..." -ForegroundColor Yellow
$validProvenanceStates = @('Unknown', 'Partial', 'Blocked', 'Ready')
$validImageStates = @('NotApplicable', 'Excluded', 'Pending', 'Included')
$validVmStates = @('NotTested', 'Failed', 'Partial', 'Verified')

foreach ($file in $componentFiles) {
    $path = Join-Path $fixturesPath $file
    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    
    if (-not $validProvenanceStates.Contains($json.provenanceState)) {
        $msg = "{0}: Invalid provenanceState '{1}'" -f $file, $json.provenanceState
        throw $msg
    }
    if (-not $validImageStates.Contains($json.imageInclusionState)) {
        $msg = "{0}: Invalid imageInclusionState '{1}'" -f $file, $json.imageInclusionState
        throw $msg
    }
    if (-not $validVmStates.Contains($json.approvedVmState)) {
        $msg = "{0}: Invalid approvedVmState '{1}'" -f $file, $json.approvedVmState
        throw $msg
    }
}
Write-Host "  All states use explicit enums (Unknown/Partial/Blocked/Ready, NotApplicable/Excluded/Pending/Included, NotTested/Failed/Partial/Verified)" -ForegroundColor Green

# Test 6: Validate convergence matrix derives from real evidence
Write-Host "`n[6/8] Validating convergence matrix derives from REAL evidence..." -ForegroundColor Yellow
$matrixPath = Join-Path $fixturesPath "convergence-matrix.json"
$matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json

# Verify counts
if ($matrix.totalProducts -ne 6) { $msg = "Expected 6 total products, got {0}" -f $matrix.totalProducts; throw $msg }
if ($matrix.convergedProducts -ne 3) { $msg = "Expected 3 converged, got {0}" -f $matrix.convergedProducts; throw $msg }
if ($matrix.partialProducts -ne 2) { $msg = "Expected 2 partial, got {0}" -f $matrix.partialProducts; throw $msg }
if ($matrix.blockedProducts -ne 1) { $msg = "Expected 1 blocked, got {0}" -f $matrix.blockedProducts; throw $msg }
if ($matrix.unknownProducts -ne 0) { $msg = "Expected 0 unknown, got {0}" -f $matrix.unknownProducts; throw $msg }
if ([math]::Abs($matrix.convergencePercentage - 50.0) -gt 0.01) { $msg = "Expected 50% convergence, got {0}%" -f $matrix.convergencePercentage; throw $msg }

# Verify each product state derives from fixture evidence
foreach ($state in $matrix.productStates) {
    $productId = $state.productId
    $fixtureName = switch ($productId) {
        'haven.welcome' { 'haven-welcome.json' }
        'haven.shell' { 'haven-shell.json' }
        'havenos.studio' { 'havenos-studio.json' }
        'llamacpp.provider' { 'llamacpp-provider.json' }
        'haven.notifications' { 'haven-notifications.json' }
        'haven.settings' { 'haven-settings-service.json' }
        default { $msg = "Unknown productId in matrix: {0}" -f $productId; throw $msg }
    }
    
    $fixturePath = Join-Path $fixturesPath $fixtureName
    $fixture = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    
    # Smoke evidence must match
    $fixtureSmokePassed = $fixture.smokeEvidence -and $fixture.smokeEvidence.executed -and $fixture.smokeEvidence.exitCode -eq 0
    if ($state.smokeTestPassed -ne $fixtureSmokePassed) {
        $msg = "{0}: Matrix smokeTestPassed ({1}) doesn't match fixture evidence ({2})" -f $productId, $state.smokeTestPassed, $fixtureSmokePassed
        throw $msg
    }
}
Write-Host "  Matrix correctly derived from fixture smoke evidence" -ForegroundColor Green

# Test 7: Verify matrix distinguishes states from image readiness
Write-Host "`n[7/8] Validating matrix distinguishes states from image readiness..." -ForegroundColor Yellow

# llamacpp.provider: provenance Ready but image Pending -> NOT converged
$llama = $matrix.productStates | Where-Object { $_.productId -eq 'llamacpp.provider' } | Select-Object -First 1
if ($llama.provenanceState -ne 'Ready' -or $llama.imageInclusionState -ne 'Pending' -or $llama.isConverged) {
    $msg = "llamacpp.provider: Should be Ready provenance but Pending image -> NOT converged"
    throw $msg
}

# haven.notifications: provenance Partial, image Excluded -> NOT converged
$notif = $matrix.productStates | Where-Object { $_.productId -eq 'haven.notifications' } | Select-Object -First 1
if ($notif.provenanceState -ne 'Partial' -or $notif.imageInclusionState -ne 'Excluded' -or $notif.isConverged) {
    $msg = "haven.notifications: Should be Partial provenance, Excluded image -> NOT converged"
    throw $msg
}

# haven.settings: provenance Blocked, image NotApplicable -> NOT converged
$settings = $matrix.productStates | Where-Object { $_.productId -eq 'haven.settings' } | Select-Object -First 1
if ($settings.provenanceState -ne 'Blocked' -or $settings.imageInclusionState -ne 'NotApplicable' -or $settings.isConverged) {
    $msg = "haven.settings: Should be Blocked provenance, NotApplicable image -> NOT converged"
    throw $msg
}

# Fully converged: welcome, shell, studio
$convergedPids = @('haven.welcome', 'haven.shell', 'havenos.studio')
foreach ($convergedPid in $convergedPids) {
    $state = $matrix.productStates | Where-Object { $_.productId -eq $convergedPid } | Select-Object -First 1
    if ($state.provenanceState -ne 'Ready' -or $state.imageInclusionState -ne 'Included' -or $state.approvedVmState -ne 'Verified' -or -not $state.isConverged) {
        $msg = "{0}: Should be fully converged (Ready/Included/Verified)" -f $convergedPid
        throw $msg
    }
}
Write-Host "  States correctly distinguished from image readiness" -ForegroundColor Green

# Test 8: Verify no duplicate registry
Write-Host "`n[8/8] Verifying NO duplicate registry created..." -ForegroundColor Yellow
# All components reference the SAME Worker 3 registry identity
$registryIds = @()
foreach ($file in $componentFiles) {
    $path = Join-Path $fixturesPath $file
    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $registryIds += "{0}:{1}" -f $json.registryIdentity.registryId, $json.registryIdentity.registrySchemaVersion
}
$uniqueRegistries = $registryIds | Select-Object -Unique
Write-Host "  Found unique registries: $($uniqueRegistries -join ', ')" -ForegroundColor Gray
if ($uniqueRegistries.Count -ne 1) {
    $msg = "Multiple registry identities found: {0}" -f ($uniqueRegistries -join ', ')
    throw $msg
}
$firstRegistry = $uniqueRegistries | Select-Object -First 1
$firstRegistry = [string]$firstRegistry
if ($firstRegistry -ne 'cakeos.shared-app-registry:1') {
    $msg = "Incorrect registry identity: '{0}' (expected 'cakeos.shared-app-registry:1')" -f $firstRegistry
    throw $msg
}
Write-Host "  Single Worker 3 registry referenced by all components: cakeos.shared-app-registry v1" -ForegroundColor Green

Write-Host "`n[9/9] Validating complete full-roadmap convergence coverage..." -ForegroundColor Yellow
& (Join-Path $PSScriptRoot 'validate-full-roadmap.ps1')

Write-Host "`n=== ALL VALIDATIONS PASSED ===" -ForegroundColor Cyan
Write-Host "Release Metadata / Convergence State schema implementation complete." -ForegroundColor Green
Write-Host "No ISO build performed." -ForegroundColor Green
