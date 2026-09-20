param(
    [string]$HavenUiProjectPath
)

$ErrorActionPreference = 'Stop'

$boards = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $boards)
$sharedHuiCandidate = Join-Path $repositoryRoot 'HUI/Haven.UI/Haven.UI.csproj'
$testProject = Join-Path $boards 'hui-tests/CakeOS.Apps.Boards.Hui.Tests.csproj'
$generativeGate = Join-Path $PSScriptRoot 'verify-generative-board.ps1'
$collaborationGate = Join-Path $PSScriptRoot 'verify-collaboration-boundary.ps1'

if ([string]::IsNullOrWhiteSpace($HavenUiProjectPath)) {
    if (Test-Path -LiteralPath $sharedHuiCandidate -PathType Leaf) {
        $HavenUiProjectPath = $sharedHuiCandidate
    }
    else {
        throw 'A real Haven.UI.csproj is required. Pass -HavenUiProjectPath or land the shared runtime at HUI/Haven.UI/Haven.UI.csproj.'
    }
}

$resolvedHuiProject = (Resolve-Path -LiteralPath $HavenUiProjectPath).Path
if (-not (Test-Path -LiteralPath $resolvedHuiProject -PathType Leaf)) {
    throw "Haven UI project does not exist: $resolvedHuiProject"
}
if ([System.IO.Path]::GetFileName($resolvedHuiProject) -ne 'Haven.UI.csproj') {
    throw "Expected Haven.UI.csproj, received: $resolvedHuiProject"
}

$huiProjectText = Get-Content -LiteralPath $resolvedHuiProject -Raw
if ($huiProjectText -notmatch '<Project\s+Sdk="Microsoft\.NET\.Sdk"' -or
    $huiProjectText -match '<PackageReference[^>]+Avalonia') {
    throw 'The supplied project does not match the platform-neutral Haven.UI project boundary.'
}

if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) {
    throw "HUI scene test project is missing: $testProject"
}
if (-not (Test-Path -LiteralPath $generativeGate -PathType Leaf)) {
    throw "Generative Boards safety gate is missing: $generativeGate"
}
if (-not (Test-Path -LiteralPath $collaborationGate -PathType Leaf)) {
    throw "Collaboration Boards safety gate is missing: $collaborationGate"
}

& $generativeGate
& $collaborationGate

Write-Host "Testing Haven Boards against real HUI project: $resolvedHuiProject"
& dotnet test $testProject --configuration Debug "-p:HavenUiProjectPath=$resolvedHuiProject"
if ($LASTEXITCODE -ne 0) {
    throw "Haven Boards HUI compatibility tests failed with exit code $LASTEXITCODE."
}

Write-Host 'Haven Boards real-HUI compatibility tests passed.'
