$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $root 'havenos.lock') -Raw | ConvertFrom-Json
$required = @('platform/gnome-shell', 'platform/mutter', 'platform/ubuntu/manifests/apt-sources.list', 'platform/ubuntu/image/live-build/auto/config', 'HUI', 'apps', 'compatibility/wine', 'compatibility/android', 'image/build-live-iso.sh', 'tooling/HavenOS.Studio/HavenOS.Studio.csproj', 'vm/havenos-dev.json')
$missing = $required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $root $_)) }
if ($missing) { throw "Missing required HavenOS paths: $($missing -join ', ')" }
if ($lock.ubuntu.baseDesktopIso.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Ubuntu ISO provenance hash is invalid.' }
foreach ($name in 'gnome-shell', 'mutter') {
  $component = $lock.components.$name
  $actual = (git -C (Join-Path $root $component.path) rev-parse HEAD).Trim()
  if ($actual -ne $component.revision) { throw "$name revision does not match havenos.lock." }
}
Write-Output 'HavenOS workspace provenance and layout verification passed.'
