[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OriginalKitRoot,
    [Parameter(Mandatory = $true)][string]$OwningHomePublishDirectory,
    [string]$OwningHomeExecutableName = 'AvaloniaHome.exe'
)
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot 'original-windows-native-kit.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.architecture -cne 'win-x64' -or $manifest.nativeAbi -ne 3) {
    throw 'The original native staging recipe requires the exact Windows x64 ABI3 manifest.'
}
if ([IO.Path]::GetFileName($OwningHomeExecutableName) -cne $OwningHomeExecutableName -or
    $OwningHomeExecutableName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*\.exe$') {
    throw 'Supply the actual owning Windows executable file name, without a path.'
}
$ownerStem = [IO.Path]::GetFileNameWithoutExtension($OwningHomeExecutableName)
$ownerDepsName = "$ownerStem.deps.json"
$kit = (Resolve-Path -LiteralPath $OriginalKitRoot).Path
$publish = (Resolve-Path -LiteralPath $OwningHomePublishDirectory).Path
if ($kit -eq $publish) { throw 'The original native kit must remain separate from the owning published artifact.' }
foreach ($required in @($OwningHomeExecutableName, $ownerDepsName, 'HavenOS.Canvas.Host.dll', 'HavenOS.Canvas.NativeUI.dll', 'HavenOS.Canvas.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) {
        throw "Supply the actual normal published Windows Home graph including Canvas: missing $required."
    }
}
$ownerDependencies = Get-Content -LiteralPath (Join-Path $publish $ownerDepsName) -Raw | ConvertFrom-Json
if ($ownerDependencies.runtimeTarget.name -notmatch '/win-x64$') {
    throw 'The owning Home dependency manifest must identify an actual win-x64 publish.'
}
if (-not @($ownerDependencies.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'HavenOS.Canvas.Host/*' }).Count) {
    throw 'The actual owning Home dependency manifest must include the Canvas host.'
}
# Staging validates/copies immutable Windows bytes and can run on the cloud host.
# Real application launch, Home admission and native smoke still require Windows.
$ownerStream = [IO.File]::OpenRead((Join-Path $publish $OwningHomeExecutableName))
try {
    $header = [byte[]]::new(64)
    if ($ownerStream.Read($header, 0, $header.Length) -ne $header.Length -or $header[0] -ne 0x4d -or $header[1] -ne 0x5a) {
        throw 'The owning Windows executable is not a PE artifact.'
    }
    $peOffset = [BitConverter]::ToInt32($header, 0x3c)
    if ($peOffset -lt 64 -or $peOffset -gt $ownerStream.Length - 6) {
        throw 'The owning Windows executable has an invalid PE header offset.'
    }
    $null = $ownerStream.Seek($peOffset, [IO.SeekOrigin]::Begin)
    $peHeader = [byte[]]::new(6)
    if ($ownerStream.Read($peHeader, 0, $peHeader.Length) -ne $peHeader.Length -or
        [BitConverter]::ToUInt32($peHeader, 0) -ne 0x00004550 -or
        [BitConverter]::ToUInt16($peHeader, 4) -ne 0x8664) {
        throw 'The owning Windows executable must be an actual x64 PE artifact.'
    }
}
finally { $ownerStream.Dispose() }
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
    ownerExecutable = $OwningHomeExecutableName; ownerDependencyManifest = $ownerDepsName;
    target = 'win-x64'; stagingHost = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription;
    archiveSha256 = $manifest.archiveSha256; files = @($actual); qualification = $manifest.qualification } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $publish 'CanvasOriginalNativeStageReceipt.json') -Encoding utf8NoBOM
