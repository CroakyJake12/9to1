# Copy firmware for this laptop into a portable source tree. Never changes drivers.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destination,
    [string]$DriverStore = "$env:SystemRoot/System32/DriverStore/FileRepository"
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $DriverStore).Path
$target = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $target) { throw 'Use a new destination; an existing export is never overwritten.' }
if ($target.StartsWith($source.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The destination must be outside the Windows DriverStore.' }
$required = @('adsp_dtbs.elf','adspr.jsn','adsps.jsn','adspua.jsn','cdsp_dtbs.elf','cdspr.jsn','qcadsp8380.mbn','qccdsp8380.mbn','qcdxkmsuc8380.mbn')
$optional = @('battmgr.jsn','hmtbtfw20.tlv','hmtbtfw20.ver','wlanfw20.mbn','wlanfw.mbn','wpss.mbn','CAMERA_ICP.mbn')
$files = @(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object { $_.Name -in ($required + $optional) })
$missing = @($required | Where-Object { $_ -notin $files.Name })
if ($missing.Count) { throw "Required firmware not found: $($missing -join ', '). No export was created." }
New-Item -ItemType Directory -Path $target | Out-Null
$records = foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart('\','/')
    $exportRelative = Join-Path 'Windows/System32/DriverStore/FileRepository' $relative
    $copy = Join-Path $target $exportRelative
    New-Item -ItemType Directory -Path (Split-Path $copy) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $copy
    $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $copyHash = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sourceHash -ne $copyHash) { throw "Copy verification failed: $relative" }
    [ordered]@{ File=$exportRelative.Replace('\','/'); Name=$file.Name; Source=$file.FullName; Bytes=$file.Length; LastWriteTimeUtc=$file.LastWriteTimeUtc.ToString('o'); SHA256=$copyHash }
}
$bios = Get-ItemProperty 'HKLM:\HARDWARE\DESCRIPTION\System\BIOS' -ErrorAction SilentlyContinue
$manifest = [ordered]@{
    SchemaVersion=1; CreatedAtUtc=(Get-Date).ToUniversalTime().ToString('o')
    Model=$bios.SystemProductName; Board=$bios.BaseBoardProduct
    Purpose='Private firmware source for this laptop; do not publish these Windows firmware files.'
    Installed=$false; HardwareValidated=$false
    RequiredNames=$required; OptionalNamesMissing=@($optional | Where-Object { $_ -notin $files.Name })
    Files=@($records)
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $target 'manifest.json') -Encoding utf8
@'
This is a private, hash-verified copy of the laptop's Windows firmware source.
It preserves DriverStore paths and file timestamps for qcom-firmware-extract.
After booting the matched Samsung kernel, pass this export root to the reviewed
qcom-firmware-extract tool. Do not copy arbitrary Wi-Fi or Bluetooth firmware into
Linux search paths: the optional files are preserved for model-specific mapping.
The export has not installed drivers or validated any Linux hardware operation.
Do not commit or upload this directory to the source repository.
'@ | Set-Content -LiteralPath (Join-Path $target 'README.txt') -Encoding utf8
Write-Output "Exported and verified $($records.Count) files to $target"
