[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('windows', 'linux', 'all')]
    [string]$Target,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$desktopProject = Join-Path $root '9to1 Workspace\shared\src\Haven.Desktop\Haven.Desktop.csproj'
$previewAssemblyName = 'NineToOne.LegacyDesktopPreview'
$previewExecutableName = '9-1'
$previewDisplayName = '9-1 Legacy Desktop Preview'
$previewPackageName = '9-1-legacy-desktop-preview'
$previewWindowsAppId = '9to1.LegacyDesktopPreview'
$revision = (& git -C $root rev-parse --short=12 HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revision)) {
    throw 'Unable to determine the Git revision for developer-preview provenance.'
}
& git -C $root diff --quiet
$unstagedChanges = $LASTEXITCODE -ne 0
& git -C $root diff --cached --quiet
$stagedChanges = $LASTEXITCODE -ne 0
$sourceState = if ($unstagedChanges -or $stagedChanges) { 'dirty' } else { 'clean' }

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\developer-preview'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$windowsOutput = Join-Path $OutputDirectory 'windows\win-x64'
$linuxOutput = Join-Path $OutputDirectory 'linux\linux-x64'
$packageOutput = Join-Path $OutputDirectory 'packages'
$packageVersion = "0.1.0+git$revision"

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function New-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)
    [void][IO.Directory]::CreateDirectory($Path)
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Set-TarString {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Header,
        [Parameter(Mandatory = $true)][int]$Offset,
        [Parameter(Mandatory = $true)][int]$Length,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value
    )

    $bytes = [Text.Encoding]::ASCII.GetBytes($Value)
    if ($bytes.Length -gt $Length) {
        throw "Tar value is too long: $Value"
    }
    [Array]::Copy($bytes, 0, $Header, $Offset, $bytes.Length)
}

function Set-TarOctal {
    param(
        [Parameter(Mandatory = $true)][byte[]]$Header,
        [Parameter(Mandatory = $true)][int]$Offset,
        [Parameter(Mandatory = $true)][int]$Length,
        [Parameter(Mandatory = $true)][Int64]$Value
    )

    $digits = [Convert]::ToString($Value, 8)
    if ($digits.Length -gt $Length - 1) {
        throw "Tar numeric value is too large: $Value"
    }
    Set-TarString -Header $Header -Offset $Offset -Length ($Length - 1) -Value $digits.PadLeft($Length - 1, '0')
}

function Write-TarHeader {
    param(
        [Parameter(Mandatory = $true)][IO.Stream]$Archive,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][Int64]$Size,
        [Parameter(Mandatory = $true)][int]$Mode,
        [Parameter(Mandatory = $true)][char]$Type
    )

    $name = $Path
    $prefix = ''
    if ([Text.Encoding]::ASCII.GetByteCount($name) -gt 100) {
        $separator = $Path.LastIndexOf('/', 155)
        if ($separator -le 0) {
            throw "Tar path cannot fit in ustar fields: $Path"
        }
        $prefix = $Path.Substring(0, $separator)
        $name = $Path.Substring($separator + 1)
    }
    if ([Text.Encoding]::ASCII.GetByteCount($name) -gt 100 -or [Text.Encoding]::ASCII.GetByteCount($prefix) -gt 155) {
        throw "Tar path cannot fit in ustar fields: $Path"
    }

    $header = [byte[]]::new(512)
    Set-TarString -Header $header -Offset 0 -Length 100 -Value $name
    Set-TarOctal -Header $header -Offset 100 -Length 8 -Value $Mode
    Set-TarOctal -Header $header -Offset 108 -Length 8 -Value 0
    Set-TarOctal -Header $header -Offset 116 -Length 8 -Value 0
    Set-TarOctal -Header $header -Offset 124 -Length 12 -Value $Size
    Set-TarOctal -Header $header -Offset 136 -Length 12 -Value ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())
    for ($index = 148; $index -lt 156; $index++) { $header[$index] = 32 }
    $header[156] = [byte][char]$Type
    Set-TarString -Header $header -Offset 257 -Length 6 -Value ("ustar" + [char]0)
    Set-TarString -Header $header -Offset 263 -Length 2 -Value '00'
    Set-TarString -Header $header -Offset 265 -Length 32 -Value 'root'
    Set-TarString -Header $header -Offset 297 -Length 32 -Value 'root'
    Set-TarString -Header $header -Offset 345 -Length 155 -Value $prefix
    $checksum = 0
    foreach ($value in $header) { $checksum += $value }
    Set-TarString -Header $header -Offset 148 -Length 8 -Value (([Convert]::ToString($checksum, 8)).PadLeft(6, '0') + [char]0 + ' ')
    $Archive.Write($header, 0, $header.Length)
}

function New-TarGzipArchive {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path,
        [string[]]$ExecutablePaths = @()
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $executables = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($executable in $ExecutablePaths) { [void]$executables.Add($executable.Replace('\', '/')) }
    $output = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $gzip = [IO.Compression.GzipStream]::new($output, [IO.Compression.CompressionLevel]::Optimal, $true)
        try {
            $entries = @([IO.DirectoryInfo]::new($rootFull)) + @(Get-ChildItem -LiteralPath $rootFull -Recurse -Force | Sort-Object FullName)
            foreach ($entry in $entries) {
                $relative = $entry.FullName.Substring($rootFull.Length).TrimStart([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
                if ([string]::IsNullOrEmpty($relative)) { $relative = '.' }
                if ($entry -is [IO.DirectoryInfo]) {
                    Write-TarHeader -Archive $gzip -Path ($relative.TrimEnd('/') + '/') -Size 0 -Mode 493 -Type '5'
                    continue
                }
                $mode = if ($executables.Contains($relative)) { 493 } else { 420 }
                Write-TarHeader -Archive $gzip -Path $relative -Size $entry.Length -Mode $mode -Type '0'
                $input = [IO.File]::OpenRead($entry.FullName)
                try {
                    $buffer = [byte[]]::new(65536)
                    while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        $gzip.Write($buffer, 0, $read)
                    }
                }
                finally {
                    $input.Dispose()
                }
                $padding = (512 - ($entry.Length % 512)) % 512
                if ($padding -gt 0) { $gzip.Write([byte[]]::new($padding), 0, $padding) }
            }
            $gzip.Write([byte[]]::new(1024), 0, 1024)
        }
        finally {
            $gzip.Dispose()
        }
    }
    finally {
        $output.Dispose()
    }
}

function Write-ArMember {
    param(
        [Parameter(Mandatory = $true)][IO.Stream]$Archive,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $source = [IO.FileInfo]::new($Path)
    if (-not $source.Exists) {
        throw "Missing Debian archive member: $Path"
    }
    $header = ('{0,-16}{1,-12}{2,-6}{3,-6}{4,-8}{5,-10}' -f "$Name/", 0, 0, 0, '100644', $source.Length) + [char]96 + "`n"
    if ([Text.Encoding]::ASCII.GetByteCount($header) -ne 60) {
        throw "Invalid ar header length for $Name."
    }
    $headerBytes = [Text.Encoding]::ASCII.GetBytes($header)
    $Archive.Write($headerBytes, 0, $headerBytes.Length)

    $input = [IO.File]::OpenRead($source.FullName)
    try {
        $buffer = [byte[]]::new(65536)
        while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $Archive.Write($buffer, 0, $read)
        }
    }
    finally {
        $input.Dispose()
    }
    if (($source.Length % 2) -ne 0) {
        $Archive.WriteByte(10)
    }
}

function New-DebianArchive {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ControlArchive,
        [Parameter(Mandatory = $true)][string]$DataArchive
    )

    $debianBinary = Join-Path ([IO.Path]::GetDirectoryName($Path)) '.debian-binary'
    Write-Utf8File -Path $debianBinary -Content "2.0`n"
    $archive = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $magic = [Text.Encoding]::ASCII.GetBytes("!<arch>`n")
        $archive.Write($magic, 0, $magic.Length)
        Write-ArMember -Archive $archive -Name 'debian-binary' -Path $debianBinary
        Write-ArMember -Archive $archive -Name 'control.tar.gz' -Path $ControlArchive
        Write-ArMember -Archive $archive -Name 'data.tar.gz' -Path $DataArchive
    }
    finally {
        $archive.Dispose()
        Remove-Item -LiteralPath $debianBinary -Force -ErrorAction SilentlyContinue
    }
}

function Test-DebianArchive {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [IO.File]::OpenRead($Path)
    try {
        $magic = [byte[]]::new(8)
        if ($stream.Read($magic, 0, $magic.Length) -ne $magic.Length -or [Text.Encoding]::ASCII.GetString($magic) -ne "!<arch>`n") {
            throw 'The generated package is not an ar archive.'
        }
        $members = [Collections.Generic.List[string]]::new()
        while ($stream.Position -lt $stream.Length) {
            $header = [byte[]]::new(60)
            $read = 0
            while ($read -lt $header.Length) {
                $chunk = $stream.Read($header, $read, $header.Length - $read)
                if ($chunk -le 0) { throw 'The generated package has a truncated ar member header.' }
                $read += $chunk
            }
            if ($header[58] -ne [byte][char]96 -or $header[59] -ne 10) {
                throw 'The generated package has an invalid ar member header.'
            }
            $name = [Text.Encoding]::ASCII.GetString($header, 0, 16).Trim().TrimEnd('/')
            $size = [Int64]::Parse([Text.Encoding]::ASCII.GetString($header, 48, 10).Trim())
            if ($size -lt 0 -or $stream.Position + $size -gt $stream.Length) {
                throw "The generated package has an invalid ar member size for $name."
            }
            [void]$members.Add($name)
            [void]$stream.Seek($size + ($size % 2), [IO.SeekOrigin]::Current)
        }
        if (($members -join ',') -ne 'debian-binary,control.tar.gz,data.tar.gz') {
            throw "The generated package has an invalid Debian member order: $($members -join ',')."
        }
    }
    finally {
        $stream.Dispose()
    }
    $dpkgDeb = Get-Command dpkg-deb -ErrorAction SilentlyContinue
    if ($null -ne $dpkgDeb) {
        & $dpkgDeb.Source --info $Path | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw 'dpkg-deb rejected the generated package.'
        }
    }
}

function New-DeveloperPreviewDeb {
    param([Parameter(Mandatory = $true)][string]$PublishDirectory)

    New-Directory $packageOutput
    Get-ChildItem -LiteralPath $packageOutput -Filter 'haven-desktop-preview_*.deb*' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force
    $work = Join-Path ([IO.Path]::GetTempPath()) ("9to1-deb-" + [guid]::NewGuid().ToString('N'))
    $controlRoot = Join-Path $work 'control'
    $dataRoot = Join-Path $work 'data'
    $controlArchive = Join-Path $work 'control.tar.gz'
    $dataArchive = Join-Path $work 'data.tar.gz'
    $debPath = Join-Path $packageOutput "$previewPackageName`_$packageVersion`_amd64.deb"
    try {
        New-Directory $controlRoot
        $applicationRoot = Join-Path $dataRoot "usr\lib\$previewPackageName"
        $launcherRoot = Join-Path $dataRoot 'usr\bin'
        $documentationRoot = Join-Path $dataRoot "usr\share\doc\$previewPackageName"
        New-Directory $applicationRoot
        New-Directory $launcherRoot
        New-Directory $documentationRoot
        Copy-Item -Path (Join-Path $PublishDirectory '*') -Destination $applicationRoot -Recurse -Force

        Write-Utf8File -Path (Join-Path $controlRoot 'control') -Content @"
Package: $previewPackageName
Version: $packageVersion
Section: devel
Priority: optional
Architecture: amd64
Maintainer: 9to1 development <dev@9to1.invalid>
Depends: libc6
Description: 9-1 legacy desktop developer-preview host
 This is a non-distributable developer test artifact for the current legacy
 desktop host. It is not CUI parity, a release candidate, or a verified Linux
 runtime. Do not redistribute it until third-party notice and runtime gates pass.
"@
        Write-Utf8File -Path (Join-Path $launcherRoot $previewPackageName) -Content @"
#!/bin/sh
exec /usr/lib/$previewPackageName/$previewAssemblyName "`$@"
"@
        Write-Utf8File -Path (Join-Path $documentationRoot 'README.Debian') -Content @"
9to1 developer preview

Source revision: $revision
Source state: $sourceState

This package exists only for developer testing. It contains the existing legacy
desktop host, not a CUI renderer or a verified Dulche runtime. It must not be
redistributed until the required third-party notices, Linux runtime validation,
and CUI migration acceptance gates are complete.
"@

        New-TarGzipArchive -Root $controlRoot -Path $controlArchive
        New-TarGzipArchive -Root $dataRoot -Path $dataArchive -ExecutablePaths @(
            "usr/bin/$previewPackageName",
            "usr/lib/$previewPackageName/$previewExecutableName"
        )
        New-DebianArchive -Path $debPath -ControlArchive $controlArchive -DataArchive $dataArchive
        Test-DebianArchive -Path $debPath
        if (([IO.FileInfo]::new($debPath)).Length -le 8) {
            throw 'The generated Debian archive is empty.'
        }
        (Get-FileHash -LiteralPath $debPath -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath "$debPath.sha256" -NoNewline
        return $debPath
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Write-BuildRecord {
    param([Parameter(Mandatory = $true)][string[]]$Artifacts)

    New-Directory $OutputDirectory
    $allArtifacts = [Collections.Generic.List[string]]::new()
    $windowsExecutable = Join-Path $windowsOutput "$previewExecutableName.exe"
    if (Test-Path -LiteralPath $windowsExecutable -PathType Leaf) {
        $allArtifacts.Add($windowsExecutable)
    }
    if (Test-Path -LiteralPath $packageOutput -PathType Container) {
        Get-ChildItem -LiteralPath $packageOutput -Filter "$previewPackageName`_*.deb" -File | Sort-Object Name | ForEach-Object {
            $allArtifacts.Add($_.FullName)
        }
    }
    foreach ($artifact in $Artifacts) {
        if (-not $allArtifacts.Contains($artifact)) {
            $allArtifacts.Add($artifact)
        }
    }
    $artifactRecords = foreach ($artifact in $allArtifacts) {
        $item = [IO.FileInfo]::new($artifact)
        [ordered]@{
            path = $item.FullName
            bytes = $item.Length
            sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    [ordered]@{
        schemaVersion = 1
        kind = 'developer-preview'
        sourceRevision = $revision
        sourceState = $sourceState
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        artifacts = $artifactRecords
        releaseStatus = 'NOT_A_RELEASE'
        limitations = @(
            'Legacy desktop host only; CUI renderer and host are absent.',
            'Dulche Linux and Windows runtime proof is absent.',
            'Third-party package notice inventory is incomplete.',
            'Linux package installation and runtime were not exercised on this Windows host.'
        )
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-record.json') -Encoding utf8
}

$artifacts = [Collections.Generic.List[string]]::new()
if ($Target -in @('windows', 'all')) {
    New-Directory $windowsOutput
    Remove-Item -LiteralPath (Join-Path $windowsOutput 'Haven.exe') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $windowsOutput "$previewExecutableName.exe") -Force -ErrorAction SilentlyContinue
    Invoke-DotNet -Arguments @(
        'publish', $desktopProject, '-c', 'Release', '-f', 'net10.0-windows10.0.19041.0', '-r', 'win-x64', '--self-contained', 'true', "-p:AssemblyName=$previewAssemblyName", "-p:ProductDisplayName=$previewDisplayName", "-p:ProductWindowsAppId=$previewWindowsAppId",
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $windowsOutput, '--nologo'
    )
    $publishedExecutable = Join-Path $windowsOutput "$previewAssemblyName.exe"
    $executable = Join-Path $windowsOutput "$previewExecutableName.exe"
    Move-Item -LiteralPath $publishedExecutable -Destination $executable -Force
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Windows publish did not produce $executable."
    }
    $artifacts.Add($executable)
}

if ($Target -in @('linux', 'all')) {
    New-Directory $linuxOutput
    Remove-Item -LiteralPath (Join-Path $linuxOutput 'Haven') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $linuxOutput $previewExecutableName) -Force -ErrorAction SilentlyContinue
    Invoke-DotNet -Arguments @(
        'publish', $desktopProject, '-c', 'Release', '-f', 'net10.0', '-r', 'linux-x64', '--self-contained', 'true', "-p:AssemblyName=$previewAssemblyName", "-p:ProductDisplayName=$previewDisplayName", "-p:ProductWindowsAppId=$previewWindowsAppId",
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $linuxOutput, '--nologo'
    )
    $publishedLinuxExecutable = Join-Path $linuxOutput $previewAssemblyName
    $linuxExecutable = Join-Path $linuxOutput $previewExecutableName
    Move-Item -LiteralPath $publishedLinuxExecutable -Destination $linuxExecutable -Force
    if (-not (Test-Path -LiteralPath $linuxExecutable -PathType Leaf)) {
        throw "Linux publish did not produce $linuxExecutable."
    }
    $artifacts.Add((New-DeveloperPreviewDeb -PublishDirectory $linuxOutput))
}

Write-BuildRecord -Artifacts $artifacts.ToArray()
$artifacts | ForEach-Object { "Created developer-preview artifact: $_" }
