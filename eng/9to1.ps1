[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('restore', 'build', 'test', 'verify', 'package-linux', 'package-windows')]
    [string]$Action,

    [ValidateSet('core', 'cui', 'home', 'spaces', 'shared', 'dulche', 'canvas', 'boards', 'studio', 'shell', 'welcome')]
    [string]$Component = 'core',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$RequireCuiOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$projects = @{
    cui = @('9to1 OS/HUI/Cui.Tests/CakeOS.Cui.Markup.Tests.csproj')
    home = @('9to1 Workspace/Home/Tests/HavenOS.Home.Tests.csproj')
    spaces = @('9to1 Workspace/Spaces/Tests/HavenOS.Spaces.Tests.csproj')
    shared = @('9to1 Workspace/shared/Haven.sln')
}
$projects.core = @($projects.cui + $projects.home + $projects.spaces)

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $FilePath $($Arguments -join ' ')"
    }
}

function Get-TrackedCount {
    param([Parameter(Mandatory)][string]$Pattern)
    $items = @(& git -C $root ls-files $Pattern)
    if ($LASTEXITCODE -ne 0) { throw "Unable to inventory tracked $Pattern files." }
    return $items.Count
}

function Invoke-DotnetAction {
    param([Parameter(Mandatory)][string]$Verb)

    if (-not $projects.ContainsKey($Component)) {
        throw "Component '$Component' is not a .NET build set. Choose core, cui, home, spaces, or shared."
    }

    foreach ($relativeProject in $projects[$Component]) {
        $project = Join-Path $root $relativeProject
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
            throw "Required project is absent: $relativeProject"
        }

        $arguments = @($Verb, $project, '--configuration', $Configuration)
        if ($Verb -eq 'restore') {
            $arguments = @('restore', $project)
        }
        Invoke-Checked dotnet $arguments
    }
}

function Invoke-LinuxPackage {
    if (-not $IsLinux) {
        throw 'Linux packages must be built on a Linux host; no cross-platform success is inferred.'
    }

    $scripts = @{
        dulche = '9to1 OS/packaging/llamacpp/build-deb.sh'
        canvas = '9to1 OS/packaging/canvas-rnote/build-deb.sh'
        boards = '9to1 OS/packaging/boards-appflowy/build-deb.sh'
        studio = '9to1 OS/packaging/build-havenos-studio-deb.sh'
        shell = '9to1 OS/packaging/build-havenos-shell-deb.sh'
        welcome = '9to1 OS/packaging/build-haven-welcome-deb.sh'
    }
    if (-not $scripts.ContainsKey($Component)) {
        throw "No accepted Linux package recipe is registered for '$Component'."
    }

    Invoke-Checked bash @((Join-Path $root $scripts[$Component]))
}

function Invoke-RepositoryVerification {
    $generated = @(& git -C $root ls-files '*.pyc')
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect tracked generated files.' }
    if ($generated.Count -ne 0) {
        throw "Tracked Python bytecode is forbidden:`n$($generated -join "`n")"
    }

    Invoke-DotnetAction test
    Invoke-Checked powershell @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $root '9to1 OS/release/tests/validate-package-preload.ps1'))

    $cui = Get-TrackedCount '*.cui'
    $axaml = Get-TrackedCount '*.axaml'
    $hui = Get-TrackedCount '*.hui'
    Write-Host "Tracked markup: CUI=$cui AXAML=$axaml HUI=$hui"

    if ($RequireCuiOnly -and ($axaml -ne 0 -or $hui -ne 0)) {
        throw "Final CUI-only gate failed: $axaml AXAML and $hui HUI files remain tracked. Reference/donor exclusions have not yet been formalised into this strict gate."
    }

    if ($axaml -ne 0 -or $hui -ne 0) {
        Write-Warning 'CUI migration remains unfinished. Legacy markup is reported, not treated as success.'
    }
}

Push-Location $root
try {
    switch ($Action) {
        'restore' { Invoke-DotnetAction restore }
        'build' { Invoke-DotnetAction build }
        'test' { Invoke-DotnetAction test }
        'verify' { Invoke-RepositoryVerification }
        'package-linux' { Invoke-LinuxPackage }
        'package-windows' {
            if ($Component -notin @('canvas', 'boards')) {
                throw 'The current Windows package/smoke recipe covers only the legacy Canvas/Boards host.'
            }
            Invoke-Checked powershell @(
                '-NoProfile',
                '-ExecutionPolicy', 'Bypass',
                '-File', (Join-Path $root '9to1 OS/tests/windows/publish-windows.ps1'))
        }
    }
}
finally {
    Pop-Location
}
