#Requires -Version 5.1
<#
  End-to-end Windows publish for Cake Canvas/Boards (native ARM64).
  1. cargo build --release the real Rnote cdylib (needs MSVC ARM64 + vcpkg GNOME stack)
  2. dotnet publish the Windows HUI host (win-arm64, self-contained)
  3. stage native DLLs beside the exe
  4. run the headless Canvas smoke test
  Verdicts are honest: BLOCKED names the missing prerequisite.
#>
param(
  [string]$VcpkgTripletDir = 'C:\CakeOS-work\vcpkg\installed\arm64-windows-cakeos-release',
  [string]$PublishDir = 'artifacts\hui-windows-host\publish'
)

$ErrorActionPreference = 'Stop'
# Refresh PATH: fresh shells often lack user/machine additions (cargo, dotnet, CMake).
$env:Path = [System.Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
  [System.Environment]::GetEnvironmentVariable('Path', 'User') + ';' +
  (Join-Path $env:USERPROFILE '.cargo\bin') + ';' + $env:Path
$Repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$PublishFull = Join-Path $Repo $PublishDir
$VcpkgBin = Join-Path $VcpkgTripletDir 'bin'
$VcpkgPkgConfig = Join-Path $VcpkgTripletDir 'lib\pkgconfig'

function Blocked($what) { Write-Host "PUBLISH verdict=BLOCKED $what"; exit 2 }

$pkgconf = @(
  (Join-Path $VcpkgTripletDir 'tools\pkgconf\pkgconf.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $pkgconf) {
  $pkgconf = Get-ChildItem -LiteralPath 'C:\CakeOS-work\vcpkg\installed' -Recurse -Filter 'pkgconf.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName
}
if (-not $pkgconf) { Blocked 'pkgconf.exe not found (vcpkg pkgconf install incomplete)' }
if (-not (Test-Path -LiteralPath $VcpkgPkgConfig)) { Blocked "PKG_CONFIG dir missing: $VcpkgPkgConfig (vcpkg glib/cairo/pango incomplete)" }

$env:PKG_CONFIG_PATH = $VcpkgPkgConfig
$env:PATH = "$([IO.Path]::GetDirectoryName($pkgconf));$VcpkgBin;$env:PATH"

Write-Host "--- HUI vendor additions (donor-safe patch, idempotent) ---"
$pythons = @('python', 'python3', 'py') | ForEach-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -ExpandProperty Source
$patch = Join-Path $Repo 'HUI\patches\apply-canvas-hui-additions.py'
$patched = $false
foreach ($py in $pythons) {
  & $py $patch 2>&1 | Select-Object -Last 3
  if ($LASTEXITCODE -eq 0) { $patched = $true; break }
}
if (-not $patched) { Write-Host "WARNING: no Python to run HUI patch script; vendor icons must already be patched" }

Write-Host "--- cargo build (Rnote cdylib) ---"
& cargo build --release --manifest-path (Join-Path $Repo 'apps\canvas\rnote-poc\Cargo.toml') --lib
if ($LASTEXITCODE -ne 0) { Blocked "cargo build failed (exit $LASTEXITCODE)" }
$dll = Join-Path $Repo 'apps\canvas\rnote-poc\target\release\cakeos_canvas_rnote_poc.dll'
if (-not (Test-Path -LiteralPath $dll)) { Blocked 'cdylib DLL missing after cargo build' }

Write-Host "--- dotnet publish (Windows host) ---"
& dotnet publish (Join-Path $Repo 'HUI\WindowsHost\CakeOS.HuiWindowsHost.csproj') `
  -c Release -r win-arm64 --self-contained true `
  "-p:HavenUiProjectPath=$Repo\HUI\vendor\Haven.UI\Haven.UI.csproj" `
  -o $PublishFull
if ($LASTEXITCODE -ne 0) { Blocked "dotnet publish failed (exit $LASTEXITCODE)" }

Write-Host "--- stage native DLLs ---"
Copy-Item -LiteralPath $dll -Destination (Join-Path $PublishFull 'cakeos_canvas_rnote_poc.dll') -Force
$vcpkgDlls = Get-ChildItem -LiteralPath $VcpkgBin -Filter '*.dll' -ErrorAction SilentlyContinue
foreach ($d in $vcpkgDlls) { Copy-Item -LiteralPath $d.FullName -Destination $PublishFull -Force }
Write-Host "staged $($vcpkgDlls.Count) vcpkg runtime DLLs"

Write-Host "--- smoke test ---"
& (Join-Path $Repo 'tests\windows\canvas-smoke.ps1') -PublishDir $PublishFull
