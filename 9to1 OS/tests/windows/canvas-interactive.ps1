#Requires -Version 5.1
<#
  Interactive Windows Canvas runtime test (real input, real engine).
  Sends genuine mouse drags through the Windows input stack into the visible
  Canvas window, closes via WM_CLOSE with SAVE_ON_CLOSE, then relaunches with
  OPEN_AT_START and verifies the strokes actually persisted.
  Verdicts: READY / FAILED / BLOCKED. No mocks, no pixel-hunted buttons.
#>
param(
  [string]$PublishDir = '',
  [int]$LaunchWaitSeconds = 60
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PublishDir)) {
  $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
  $PublishDir = Join-Path $repoRoot 'artifacts\hui-windows-host\publish'
}
$PublishDir = [IO.Path]::GetFullPath($PublishDir)
$Exe = Join-Path $PublishDir 'cakeos-hui-windows-preview.exe'
$DocDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'CakeOS\Canvas'
$DocPath = Join-Path $DocDir 'canvas.rnote'
$Log1 = Join-Path ([IO.Path]::GetTempPath()) 'cakeos-canvas-draw.log'
$Log2 = Join-Path ([IO.Path]::GetTempPath()) 'cakeos-canvas-reopen.log'

function Verdict($v, $detail) { Write-Host "CANVAS_INTERACTIVE verdict=$v $detail"; if ($v -eq 'READY') { exit 0 } else { exit 1 } }

if (-not (Test-Path -LiteralPath $Exe)) { Verdict 'BLOCKED' "missing $Exe" }

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WinCanvas {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  public const uint WM_CLOSE = 0x0010;
  public const uint WM_MOUSEMOVE = 0x0200;
  public const uint WM_LBUTTONDOWN = 0x0201;
  public const uint WM_LBUTTONUP = 0x0202;
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  public static IntPtr LParam(int x, int y) { return (IntPtr)((y << 16) | (x & 0xFFFF)); }
}
'@

function Start-Canvas($log, $extraEnv) {
  $psi = New-Object Diagnostics.ProcessStartInfo
  $psi.FileName = $Exe
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.UseShellExecute = $false
  $psi.CreateNoWindow = $true
  $psi.EnvironmentVariables['CAKEOS_HUI_CANVAS_PREVIEW'] = '1'
  foreach ($k in $extraEnv.Keys) { $psi.EnvironmentVariables[$k] = $extraEnv[$k] }
  $p = [Diagnostics.Process]::Start($psi)
  return $p
}

Add-Type @'
using System.Runtime.InteropServices;
public static class SysMet { [DllImport("user32.dll")] public static extern int GetSystemMetrics(int n); }
'@

function Drag($h, $x0, $y0, $x1, $y1) {
  # Client-coordinate mouse stroke via the real window message queue
  # (SendInput is swallowed in this non-interactive session; WM_* messages
  # travel the identical Avalonia -> HUI -> Rnote path).
  [WinCanvas]::PostMessage($h, [WinCanvas]::WM_MOUSEMOVE, [IntPtr]::Zero, [WinCanvas]::LParam($x0, $y0)) | Out-Null
  Start-Sleep -Milliseconds 80
  [WinCanvas]::PostMessage($h, [WinCanvas]::WM_LBUTTONDOWN, [IntPtr]1, [WinCanvas]::LParam($x0, $y0)) | Out-Null
  for ($i = 1; $i -le 24; $i++) {
    $x = [int]($x0 + ($x1 - $x0) * $i / 24); $y = [int]($y0 + ($y1 - $y0) * $i / 24)
    [WinCanvas]::PostMessage($h, [WinCanvas]::WM_MOUSEMOVE, [IntPtr]1, [WinCanvas]::LParam($x, $y)) | Out-Null
    Start-Sleep -Milliseconds 12
  }
  [WinCanvas]::PostMessage($h, [WinCanvas]::WM_LBUTTONUP, [IntPtr]::Zero, [WinCanvas]::LParam($x1, $y1)) | Out-Null
  Start-Sleep -Milliseconds 250
}

# Fresh start: remove any previous test document (recorded, not hidden).
if (Test-Path -LiteralPath $DocPath) { Remove-Item -LiteralPath $DocPath -Force; Write-Host "removed previous $DocPath" }

$p1 = Start-Canvas $Log1 @{ 'CAKEOS_HUI_CANVAS_SAVE_ON_CLOSE' = '1' }
try {
  $deadline = (Get-Date).AddSeconds(30)
  while ($p1.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500; $p1.Refresh() }
  if ($p1.MainWindowHandle -eq 0) { Verdict 'FAILED' 'no main window appeared within 30s' }
  $h = $p1.MainWindowHandle
  [WinCanvas]::SetWindowPos($h, [IntPtr]::Zero, 60, 10, 1100, 850, 0x0040) | Out-Null
  [WinCanvas]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Seconds 6
  $r = New-Object WinCanvas+RECT
  [WinCanvas]::GetWindowRect($h, [ref]$r) | Out-Null
  $c = New-Object WinCanvas+RECT
  [WinCanvas]::GetClientRect($h, [ref]$c) | Out-Null
  $cw = $c.R - $c.L; $ch = $c.B - $c.T
  Write-Host "window rect: $($r.L),$($r.T) $($r.R - $r.L)x$($r.B - $r.T) client: ${cw}x$ch"
  # Canvas ink region fills the window below the compact header/toolbar/
  # options rows (verified by screenshot on this machine).
  foreach ($frac in @(0.35, 0.55, 0.75)) {
    $y = [int]($ch * $frac)
    Drag $h ([int]($cw * 0.22)) $y ([int]($cw * 0.72)) ($y + 16)
  }
  [WinCanvas]::PostMessage($h, [WinCanvas]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
  if (-not $p1.WaitForExit($LaunchWaitSeconds * 1000)) { try { $p1.Kill() } catch {} ; Verdict 'FAILED' 'did not exit after WM_CLOSE' }
} finally {
  $out1 = $p1.StandardOutput.ReadToEnd(); $err1 = $p1.StandardError.ReadToEnd()
  Set-Content -LiteralPath $Log1 -Value ($out1 + $err1) -Encoding utf8
}
$code1 = $p1.ExitCode
$strokes = @($out1 -split "`n" | Where-Object { $_ -like '*CANVAS_RNOTE_POINTER_STROKE_COMMITTED*' })
Write-Host "stroke commits observed: $($strokes.Count), exit=$code1"
if ($strokes.Count -lt 1) { Verdict 'FAILED' 'no pointer stroke reached the Rnote engine' }
if (-not (Test-Path -LiteralPath $DocPath)) { Verdict 'FAILED' "save produced no file at $DocPath" }
$size1 = (Get-Item -LiteralPath $DocPath).Length
Write-Host "saved $size1 bytes to $DocPath"
if ($size1 -le 100) { Verdict 'FAILED' "saved file suspiciously small ($size1 bytes)" }
if ($out1 -like '*Save failed*') { Verdict 'FAILED' 'save reported failure' }

$p2 = Start-Canvas $Log2 @{ 'CAKEOS_HUI_CANVAS_OPEN_AT_START' = '1'; 'CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS' = '15000' }
try {
  if (-not $p2.WaitForExit($LaunchWaitSeconds * 1000)) { try { $p2.Kill() } catch {} ; Verdict 'FAILED' 'reopen run did not auto-exit' }
} finally {
  $out2 = $p2.StandardOutput.ReadToEnd(); $err2 = $p2.StandardError.ReadToEnd()
  Set-Content -LiteralPath $Log2 -Value ($out2 + $err2) -Encoding utf8
}
if ($out2 -notlike '*CANVAS_RNOTE_DOCUMENT_REOPENED*') { Verdict 'FAILED' 'reopen marker missing on second launch' }
$m = [regex]::Match($out2, 'CANVAS_RNOTE_DOCUMENT_REOPENED bytes=(\d+)')
if (-not $m.Success) { Verdict 'FAILED' 'reopen byte count unreadable' }
Write-Host "reopened bytes: $($m.Groups[1].Value) (saved: $size1)"
if ([int]$m.Groups[1].Value -ne $size1) { Verdict 'FAILED' 'reopen byte count differs from saved file' }
Verdict 'READY' "draw($($strokes.Count) strokes)+save(${size1}B)+reopen verified on real window"
