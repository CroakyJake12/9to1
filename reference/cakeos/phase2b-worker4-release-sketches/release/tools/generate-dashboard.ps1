#!/usr/bin/env pwsh
<#
.SYNOPSIS
Worker 4 Convergence Dashboard - Live view of release convergence state

.DESCRIPTION
Generates a human-readable dashboard from the convergence matrix, dependency DAG,
and admission log. Auto-refreshes from schema-validated evidence.
#>

$ErrorActionPreference = 'Stop'

# Parameters (PowerShell 5.1 compatible)
$ReleaseDir = "release"
$Watch = $false
$RefreshSeconds = 5

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$releaseDir = Join-Path $scriptDir ".."
$fixturesPath = Join-Path $releaseDir "fixtures"
$matrixPath = Join-Path $fixturesPath "convergence-matrix.json"
$dagPath = Join-Path $fixturesPath "dependency-dag.json"
$admissionLogPath = Join-Path $fixturesPath "admission-log.json"
$manifestPath = Join-Path $fixturesPath "release-manifest.json"

function Show-Dashboard {
    Clear-Host
    Write-Host "=================================================================================" -ForegroundColor Cyan
    Write-Host "         HAVENOS WORKER 4 -- CONTINUOUS RELEASE ADMISSION DASHBOARD" -ForegroundColor Cyan
    Write-Host "                      Phase 2B Integration Baseline" -ForegroundColor Cyan
    Write-Host "=================================================================================" -ForegroundColor Cyan
    Write-Host "  Baseline: phase2b/full-feature-integration @ 670915d2" -ForegroundColor Gray
    $genTime = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
    Write-Host "  Generated: $genTime UTC" -ForegroundColor Gray
    Write-Host "=================================================================================" -ForegroundColor Cyan

    # Load matrix
    if (-not (Test-Path -LiteralPath $matrixPath)) {
        Write-Host "`n[ERROR] Convergence matrix not found: $matrixPath" -ForegroundColor Red
        return
    }
    $matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json

    # Load manifest
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    # ============================================================
    # CONVERGENCE MATRIX
    # ============================================================
    Write-Host "`n+-- CONVERGENCE MATRIX ------------------------------------------------------------+" -ForegroundColor Yellow
    Write-Host "| Release: $($matrix.releaseId) v$($manifest.version)" -ForegroundColor White
    Write-Host "| Total: $($matrix.totalProducts) | Converged: $($matrix.convergedProducts) | Partial: $($matrix.partialProducts) | Blocked: $($matrix.blockedProducts) | Unknown: $($matrix.unknownProducts)" -ForegroundColor White
    $pctColor = if ($matrix.convergencePercentage -ge 80) { "Green" } elseif ($matrix.convergencePercentage -ge 50) { "Yellow" } else { "Red" }
    $pctStr = "{0:N1}%" -f $matrix.convergencePercentage
    Write-Host "| Convergence: $($pctStr.PadLeft(6))" -ForegroundColor $pctColor
    Write-Host "+------------------------------------------------------------------------------+" -ForegroundColor Yellow

    Write-Host "`n  PRODUCT ID              TYPE                PROV      IMAGE     VM        SMK OFF CONV" -ForegroundColor Gray
    Write-Host "  ---------------------------------------------------------------------------------" -ForegroundColor Gray

    foreach ($state in $matrix.productStates) {
        $prodId = $state.productId.PadRight(22)
        $prodType = $state.productType.PadRight(18)
        $prodProv = $state.provenanceState.PadRight(8)
        $prodImg = $state.imageInclusionState.PadRight(8)
        $prodVm = $state.approvedVmState.PadRight(8)
        $smk = if ($state.smokeTestPassed) { "+" } else { "-" }
        $off = if ($state.offlineRequirementMet) { "+" } else { "-" }
        $conv = if ($state.isConverged) { "+" } else { "-" }

        $lineColor = if ($state.isConverged) { "Green" } elseif ($state.provenanceState -eq 'Blocked' -or $state.approvedVmState -eq 'Failed') { "Red" } elseif ($state.provenanceState -eq 'Partial' -or $state.imageInclusionState -eq 'Pending') { "Yellow" } else { "Gray" }

        $blockReason = if ($state.blockingReason) { "  <- $($state.blockingReason)" } else { "" }
        Write-Host "  $prodId $prodType $prodProv $prodImg $prodVm   $smk   $off   $conv$blockReason" -ForegroundColor $lineColor
    }

    # ============================================================
    # DEPENDENCY DAG
    # ============================================================
    if (Test-Path -LiteralPath $dagPath) {
        $dag = Get-Content -LiteralPath $dagPath -Raw | ConvertFrom-Json
        $nodeCount = ($dag.nodes | Get-Member -MemberType NoteProperty).Count
        $edgeCount = $dag.edges.Count
        Write-Host "`n+-- DEPENDENCY DAG ----------------------------------------------------------------+" -ForegroundColor Yellow
        Write-Host "| Nodes: $nodeCount | Edges: $edgeCount" -ForegroundColor White
        Write-Host "+------------------------------------------------------------------------------+" -ForegroundColor Yellow

        Write-Host "`n  Edges (from -> to [kind]):" -ForegroundColor Gray
        foreach ($edge in $dag.edges) {
            $from = $dag.nodes.$($edge.from)
            $to = $dag.nodes.$($edge.to)
            $fromProv = if ($from) { $from.provenanceState } else { "EXTERNAL" }
            $toProv = if ($to) { $to.provenanceState } else { "EXTERNAL" }
            Write-Host "    $($edge.from) ($fromProv) --[$($edge.kind)]--> $($edge.to) ($toProv)" -ForegroundColor Gray
        }
    }

    # ============================================================
    # ADMISSION LOG (recent)
    # ============================================================
    if (Test-Path -LiteralPath $admissionLogPath) {
        $log = Get-Content -LiteralPath $admissionLogPath -Raw | ConvertFrom-Json
        Write-Host "`n+-- ADMISSION LOG (last 10) -----------------------------------------------------------+" -ForegroundColor Yellow
        Write-Host "+------------------------------------------------------------------------------+" -ForegroundColor Yellow

        $recent = $log | Select-Object -Last 10
        foreach ($entry in $recent) {
            $ts = [DateTime]::Parse($entry.timestamp).ToLocalTime().ToString('HH:mm:ss')
            $convIcon = if ($entry.convergenceStatus) { "+" } else { "-" }
            Write-Host "  [$ts] $($entry.productId) ($($entry.productType)) Prov=$($entry.provenanceState) Img=$($entry.imageInclusionState) VM=$($entry.approvedVmState) Smoke=$($entry.smokeExecuted)/$($entry.smokeExitCode) Conv=$convIcon" -ForegroundColor Gray
        }
    }

    # ============================================================
    # PRODUCT MATRIX SUMMARY
    # ============================================================
    Write-Host "`n+-- PRODUCT MATRIX (by type) ---------------------------------------------------------+" -ForegroundColor Yellow
    $types = @('App', 'SystemSurface', 'BackgroundComponent', 'Provider', 'Service')
    foreach ($type in $types) {
        $typeStates = @($matrix.productStates | Where-Object { $_.productType -eq $type })
        if ($typeStates.Count -eq 0) { continue }
        $convCount = 0
        foreach ($s in $typeStates) { if ($s.isConverged) { $convCount++ } }
        $tot = $typeStates.Count
        $typePad = $type.PadRight(20)
        $line = [string]::Format("| {0} {1}/{2} converged", $typePad, $convCount, $tot)
        Write-Host $line -ForegroundColor Gray
        foreach ($s in $typeStates) {
            $icon = if ($s.isConverged) { "+" } else { "o" }
            Write-Host "|   $icon $($s.productId) [Prov=$($s.provenanceState) Img=$($s.imageInclusionState) VM=$($s.approvedVmState)]" -ForegroundColor Gray
        }
    }
    Write-Host "+------------------------------------------------------------------------------+" -ForegroundColor Yellow

    # ============================================================
    # BLOCKERS
    # ============================================================
    $blocked = $matrix.productStates | Where-Object { $_.provenanceState -eq 'Blocked' -or $_.approvedVmState -eq 'Failed' }
    $partial = $matrix.productStates | Where-Object { $_.provenanceState -eq 'Partial' -or $_.imageInclusionState -eq 'Pending' }

    if ($blocked.Count -gt 0 -or $partial.Count -gt 0) {
        Write-Host "`n+-- BLOCKERS --------------------------------------------------------------------+" -ForegroundColor Red
        foreach ($b in $blocked) {
            Write-Host "| [BLOCKED] $($b.productId) ($($b.productType)) - $($b.blockingReason)" -ForegroundColor Red
        }
        foreach ($p in $partial) {
            Write-Host "| [PARTIAL] $($p.productId) ($($p.productType)) - $($p.blockingReason)" -ForegroundColor Yellow
        }
        Write-Host "+------------------------------------------------------------------------------+" -ForegroundColor Red
    } else {
        Write-Host "`n+-- BLOCKERS --------------------------------------------------------------------+" -ForegroundColor Green
        Write-Host "| No blockers - all components have explicit states" -ForegroundColor Green
        Write-Host "+------------------------------------------------------------------------------+" -ForegroundColor Green
    }
}

# Initial display
Show-Dashboard

if ($Watch) {
    Write-Host "`n[Watching for changes - press Ctrl+C to exit]..." -ForegroundColor Gray
    while ($true) {
        Start-Sleep -Seconds $RefreshSeconds
        Show-Dashboard
    }
}