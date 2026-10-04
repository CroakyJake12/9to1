#Requires -Version 5.1
<#
  Windows Canvas headless smoke test (non-disruptive, no reboot).
  Launches cakeos-hui-windows-preview.exe with the input self-test and an
  auto-exit timer, then asserts the real Rnote boundary markers.
  Honest verdicts: READY / FAILED / BLOCKED (native DLL not built yet).
#>
param(
  [string]$PublishDir = '',
  [int]$AutoExitMs = 12000,
  [int]$WaitSeconds = 90
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PublishDir)) {
  $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
  $PublishDir = Join-Path $repoRoot 'artifacts\hui-windows-host\publish'
}
$PublishDir = [IO.Path]::GetFullPath($PublishDir)
$Exe = Join-Path $PublishDir 'cakeos-hui-windows-preview.exe'
$Dll = Join-Path $PublishDir 'cakeos_canvas_rnote_poc.dll'
$Log = Join-Path ([IO.Path]::GetTempPath()) 'cakeos-canvas-smoke.log'

function Verdict($v, $detail) { Write-Host "CANVAS_WINDOWS_SMOKE verdict=$v $detail"; if ($v -eq 'READY') { exit 0 } else { exit 1 } }

if (-not (Test-Path -LiteralPath $Exe)) { Verdict 'BLOCKED' "missing $Exe (publish first)" }
if (-not (Test-Path -LiteralPath $Dll)) { Verdict 'BLOCKED' "missing $Dll (cargo build first)" }
if (Test-Path -LiteralPath $Log) { Remove-Item -LiteralPath $Log -Force }

$env:CAKEOS_HUI_CANVAS_PREVIEW = '1'
$env:CAKEOS_HUI_PREVIEW_SELF_TEST = '1'
$env:CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS = "$AutoExitMs"

$psi = New-Object Diagnostics.ProcessStartInfo
$psi.FileName = $Exe
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.EnvironmentVariables['CAKEOS_HUI_CANVAS_PREVIEW'] = '1'
$psi.EnvironmentVariables['CAKEOS_HUI_PREVIEW_SELF_TEST'] = '1'
$psi.EnvironmentVariables['CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS'] = "$AutoExitMs"
$proc = [Diagnostics.Process]::Start($psi)
if (-not $proc.WaitForExit($WaitSeconds * 1000)) { try { $proc.Kill() } catch {} ; Verdict 'FAILED' 'process did not auto-exit in time' }
$output = $proc.StandardOutput.ReadToEnd()
$stderr = $proc.StandardError.ReadToEnd()
Set-Content -LiteralPath $Log -Value $output -Encoding utf8
Set-Content -LiteralPath "$Log.err" -Value $stderr -Encoding utf8
if (-not $proc.HasExited) { try { $proc.Kill() } catch {} ; Verdict 'FAILED' 'process still running after wait' }
$code = $proc.ExitCode

$need = @('CANVAS_RNOTE_MANAGED_SAVE_RELOAD_READY', 'CANVAS_RNOTE_HUI_RENDER_READY', 'CANVAS_RNOTE_HUI_TOOLBAR_READY')
$missing = @($need | Where-Object { $output -notlike "*$_*" })
if ($missing.Count -gt 0) { Verdict 'FAILED' ("missing markers: " + ($missing -join ',')) }
if ($code -ne 0) { Verdict 'FAILED' "exit code $code" }
Verdict 'READY' 'real Rnote engine + HUI input + save/reload proof all observed'
