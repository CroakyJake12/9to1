$ErrorActionPreference = 'Stop'

$osRoot = Split-Path -Parent $PSScriptRoot
$rescuePath = Join-Path $osRoot 'image/book4edge16/cakeos-rescue-boot.sh'
$diagnosticsPath = Join-Path $osRoot 'image/book4edge16/cakeos-diagnostics.sh'
$rescue = Get-Content -LiteralPath $rescuePath -Raw
$diagnostics = Get-Content -LiteralPath $diagnosticsPath -Raw

foreach ($unsafePattern in @('chpasswd', 'ubuntu:cakeos-rescue', 'SSH is running')) {
    if ($rescue.Contains($unsafePattern)) {
        throw "Rescue boot script contains unsafe remote-access behavior: $unsafePattern"
    }
}

foreach ($requiredDirective in @(
        'umask 077',
        'chmod 0700 "$outdir"',
        'chmod 0600 "$archive_tmp"',
        'mv -- "$archive_tmp" "$archive"')) {
    if (-not $diagnostics.Contains($requiredDirective)) {
        throw "Diagnostics script is missing a private-output safeguard: $requiredDirective"
    }
}

Write-Output 'PASS: rescue access has no default password; diagnostic bundles use private staging and permissions.'
