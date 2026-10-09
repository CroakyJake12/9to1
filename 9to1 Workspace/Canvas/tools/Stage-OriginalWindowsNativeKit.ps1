[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OriginalKitRoot,
    [Parameter(Mandatory = $true)][string]$OwningHomePublishDirectory
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $env:OS -ne 'Windows_NT') { throw 'The original Canvas runtime bundle requires Windows.' }
$manifestPath = Join-Path $PSScriptRoot 'original-windows-native-kit.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$kit = (Resolve-Path -LiteralPath $OriginalKitRoot).Path
$publish = (Resolve-Path -LiteralPath $OwningHomePublishDirectory).Path
if ($kit -eq $publish) { throw 'The original native kit must remain separate from the owning published artifact.' }
foreach ($required in @('AvaloniaHome.exe', 'AvaloniaHome.deps.json', 'HavenOS.Canvas.Host.dll', 'HavenOS.Canvas.NativeUI.dll', 'HavenOS.Canvas.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) {
        throw "Supply the actual normal published Windows Home graph including Canvas: missing $required."
    }
}
if ((Get-Content -LiteralPath (Join-Path $publish 'AvaloniaHome.deps.json') -Raw) -notmatch 'HavenOS.Canvas.Host') {
    throw 'The actual owning Home dependency manifest must include the Canvas host.'
}
$all = @(Get-ChildItem -LiteralPath $kit -File -Recurse)
$sources = @()
foreach ($file in $manifest.files) {
    $matches = @($all | Where-Object { $_.Name -ceq $file.name })
    if ($matches.Count -ne 1) { throw "The original kit must contain exactly one $($file.name)." }
    $source = $matches[0]
    if (($source.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $source.Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
        throw "The original native kit identity differs: $($file.name)."
    }
    $target = Join-Path $publish $file.name
    if (Test-Path -LiteralPath $target) {
        if ((Get-Item -LiteralPath $target).Length -ne $file.bytes -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) {
            throw "The published owner already contains a different $($file.name); preserve it for review."
        }
    }
    $sources += [pscustomobject]@{ Source = $source.FullName; Target = $target; Identity = $file }
}
$licenseSource = Join-Path $kit 'dependency-licenses'
if (-not (Test-Path -LiteralPath $licenseSource -PathType Container) -or
    @(Get-ChildItem -LiteralPath $licenseSource -File -Recurse).Count -eq 0) {
    throw 'Supply the complete original dependency-licenses directory; the text-only projection is insufficient.'
}
$donorLicense = Join-Path $kit 'source/Rnote-GPL3-LICENSE'
$provenance = Join-Path $kit 'source/rnote-poc/PROVENANCE.md'
foreach ($originalText in @($donorLicense, $provenance)) {
    if (-not (Test-Path -LiteralPath $originalText -PathType Leaf)) { throw 'The original donor licence/source provenance is required.' }
}
$licenseTarget = Join-Path $publish 'CanvasOriginalNativeLicenses'
if (Test-Path -LiteralPath $licenseTarget) { throw 'Preserve the existing native licence custody directory and use a fresh normal publish output.' }
# Verification completes before any copy. Sources, donor, lock and licence bodies stay unchanged.
foreach ($source in $sources) { Copy-Item -LiteralPath $source.Source -Destination $source.Target }
New-Item -ItemType Directory -Path $licenseTarget | Out-Null
Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $licenseTarget 'dependency-licenses') -Recurse
Copy-Item -LiteralPath $donorLicense -Destination (Join-Path $licenseTarget 'Rnote-GPL3-LICENSE')
Copy-Item -LiteralPath $provenance -Destination (Join-Path $licenseTarget 'PROVENANCE.md')
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $publish 'CanvasOriginalNativeKit.json')
$actual = foreach ($file in Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName) {
    [ordered]@{ path = [IO.Path]::GetRelativePath($publish, $file.FullName); bytes = $file.Length;
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ status = 'OWNING_HOME_PUBLISH_NATIVE_FILES_STAGED_APPLICATION_SMOKE_UNRUN';
    archiveSha256 = $manifest.archiveSha256; files = @($actual); qualification = $manifest.qualification } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $publish 'CanvasOriginalNativeStageReceipt.json') -Encoding utf8NoBOM
