[CmdletBinding()]
param(
    [string] $EvidenceRoot = 'C:\Temp\havenos-transfer\cakeos-gate4-evidence',
    [switch] $RequireInstall
)

$ErrorActionPreference = 'Stop'

$ExpectedSha256 = '104f8ff3929b8b0e1c90673804d7cfbe79a31ea5c2763dc040246a7ead53575a'
$ExpectedVersion = '0.1.0+git2a578502c319'
$ExpectedPackageFile = 'haven-hui-preview_0.1.0+git2a578502c319_amd64.deb'

function Read-KeyValueFile([string] $Path) {
    $result = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $index = $line.IndexOf('=')
        if ($index -lt 1) {
            throw "Malformed key/value line in ${Path}: $line"
        }
        $key = $line.Substring(0, $index)
        $value = $line.Substring($index + 1)
        if ($result.ContainsKey($key)) {
            throw "Duplicate key '$key' in $Path."
        }
        $result[$key] = $value
    }
    return $result
}

function Require-File([string] $Directory, [string] $Name) {
    $path = Join-Path $Directory $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Evidence file is missing: $path"
    }
    return $path
}

function Validate-EvidenceDirectory([System.IO.DirectoryInfo] $Directory, [string] $Mode) {
    $resultPath = Require-File $Directory.FullName 'result.txt'
    $packageHashPath = Require-File $Directory.FullName 'package.sha256'
    $versionPath = Require-File $Directory.FullName 'expected-version.txt'
    $sessionPath = Require-File $Directory.FullName 'session.txt'
    $beforePath = Require-File $Directory.FullName 'platform-before.txt'
    $afterPath = Require-File $Directory.FullName 'platform-after.txt'
    $previewLog = Require-File $Directory.FullName 'preview.log'
    $exportPath = Require-File $Directory.FullName 'export-path.txt'

    $result = Read-KeyValueFile $resultPath
    if ($result[$Mode] -ne 'passed') {
        throw "$Mode evidence did not declare ${Mode}=passed: $resultPath"
    }
    if ($result['runtime_self_test'] -ne 'passed') {
        throw "Runtime self-test was not explicitly recorded as passed: $resultPath"
    }
    if ($result['platform_invariant'] -ne 'passed') {
        throw "Platform invariant was not explicitly recorded as passed: $resultPath"
    }
    if ($result['package_sha256'] -ne $ExpectedSha256) {
        throw "Result package hash mismatch in $resultPath."
    }
    if ($Mode -eq 'install' -and $result['installed_version'] -ne $ExpectedVersion) {
        throw "Installed version mismatch in $resultPath."
    }

    $version = (Get-Content -LiteralPath $versionPath -Raw).Trim()
    if ($version -ne $ExpectedVersion) {
        throw "Expected-version evidence mismatch in ${versionPath}: $version"
    }

    $hashLine = (Get-Content -LiteralPath $packageHashPath -Raw).Trim()
    if ($hashLine -notmatch ('^' + [regex]::Escape($ExpectedSha256) + '\s+')) {
        throw "Package SHA-256 evidence mismatch in $packageHashPath."
    }
    if ($hashLine -notlike "*$ExpectedPackageFile") {
        throw "Package SHA-256 evidence does not name the accepted package in $packageHashPath."
    }

    $session = Read-KeyValueFile $sessionPath
    if ($session['Class'] -ne 'user') {
        throw "Gate 4 evidence is not from a normal user session: Class='$($session['Class'])'."
    }
    if ($session['State'] -ne 'active') {
        throw "Gate 4 evidence session was not active: State='$($session['State'])'."
    }
    if ($session['Type'] -notin @('wayland', 'x11')) {
        throw "Gate 4 evidence session is not graphical: Type='$($session['Type'])'."
    }
    if ([string]::IsNullOrWhiteSpace($session['Name']) -or $session['Name'] -like 'gdm*') {
        throw "Gate 4 evidence was produced by a GDM/invalid user: Name='$($session['Name'])'."
    }
    if ($session.ContainsKey('Remote') -and $session['Remote'] -notin @('', 'no')) {
        throw "Gate 4 evidence unexpectedly came from a remote session: Remote='$($session['Remote'])'."
    }

    $before = (Get-Content -LiteralPath $beforePath -Raw).Replace("`r`n", "`n")
    $after = (Get-Content -LiteralPath $afterPath -Raw).Replace("`r`n", "`n")
    if ($before -ne $after) {
        throw "GNOME/Mutter/GDM package baseline changed during $Mode evidence run."
    }
    $platform = Read-KeyValueFile $beforePath
    foreach ($name in @('gnome-shell', 'mutter', 'gdm3')) {
        if (-not $platform.ContainsKey($name) -or [string]::IsNullOrWhiteSpace($platform[$name])) {
            throw "Missing platform version '$name' in $beforePath."
        }
    }

    $exported = (Get-Content -LiteralPath $exportPath -Raw).Trim()
    if ($exported -notmatch '/cakeos-gate4-evidence/' -or $exported -notlike "*/$($Directory.Name)") {
        throw "Export-path evidence is inconsistent in ${exportPath}: $exported"
    }

    return [pscustomobject]@{
        Mode = $Mode
        EvidenceDirectory = $Directory.FullName
        User = $session['Name']
        SessionType = $session['Type']
        PackageSha256 = $ExpectedSha256
        Version = $ExpectedVersion
        PreviewLogBytes = (Get-Item -LiteralPath $previewLog).Length
        Result = 'verified'
    }
}

if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
    throw "Gate 4 evidence root does not exist: $EvidenceRoot"
}

$probe = Get-ChildItem -LiteralPath $EvidenceRoot -Directory -Filter '*-probe' |
    Sort-Object Name -Descending |
    Select-Object -First 1
if ($null -eq $probe) {
    throw "No exported Gate 4 probe evidence exists under $EvidenceRoot."
}

$verified = @()
$verified += Validate-EvidenceDirectory $probe 'probe'

$install = Get-ChildItem -LiteralPath $EvidenceRoot -Directory -Filter '*-install' |
    Sort-Object Name -Descending |
    Select-Object -First 1
if ($RequireInstall -and $null -eq $install) {
    throw "Install evidence is required but no exported Gate 4 install evidence exists under $EvidenceRoot."
}
if ($null -ne $install) {
    if ($install.Name.Substring(0, 16) -lt $probe.Name.Substring(0, 16)) {
        throw "Latest install evidence predates the latest probe evidence; rerun install after the accepted probe."
    }
    $verified += Validate-EvidenceDirectory $install 'install'
}

$verified | ConvertTo-Json -Depth 4
