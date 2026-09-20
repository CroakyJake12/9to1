[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('restore', 'build', 'test', 'package-linux', 'package-windows', 'verify')]
    [string]$Command
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sharedSolution = Join-Path $root '9to1 Workspace\shared\Haven.sln'
$cuiProject = Join-Path $root 'framework\CUI\src\NineToOne.Cui.Markup.csproj'
$cuiTests = Join-Path $root 'framework\CUI\tests\NineToOne.Cui.Markup.Tests.csproj'
$homeProject = Join-Path $root '9to1 Workspace\Home\HavenOS.Home.csproj'
$homeTests = Join-Path $root '9to1 Workspace\Home\Tests\HavenOS.Home.Tests.csproj'

function Invoke-DotNet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-VerifiedTestSuites {
    Invoke-DotNet -Arguments @('test', $cuiTests, '-c', 'Release', '--nologo')
    Invoke-DotNet -Arguments @('test', $homeTests, '-c', 'Release', '--nologo')
    Invoke-DotNet -Arguments @('test', $sharedSolution, '-c', 'Release', '--nologo')
}

switch ($Command) {
    'restore' {
        Invoke-DotNet -Arguments @('restore', $sharedSolution, '--nologo')
        Invoke-DotNet -Arguments @('restore', $cuiProject, '--nologo')
        Invoke-DotNet -Arguments @('restore', $homeProject, '--nologo')
    }
    'build' {
        Invoke-DotNet -Arguments @('build', $cuiProject, '-c', 'Release', '--no-restore', '--nologo')
        Invoke-DotNet -Arguments @('build', $homeProject, '-c', 'Release', '--no-restore', '--nologo')
        Invoke-DotNet -Arguments @('build', $sharedSolution, '-c', 'Release', '--no-restore', '--nologo')
    }
    'test' {
        Invoke-VerifiedTestSuites
    }
    'package-linux' {
        $validator = Join-Path $root '9to1 OS\release\tests\validate-package-preload.ps1'
        & powershell -NoProfile -ExecutionPolicy Bypass -File $validator -RequireArtifacts
        if ($LASTEXITCODE -ne 0) {
            throw 'Linux package metadata or artifacts are unavailable.'
        }
        throw 'Linux packaging is blocked: the repository has no complete standalone app artifact set or image-runtime evidence.'
    }
    'package-windows' {
        throw 'Windows packaging is blocked: no shared-CUI Windows package pipeline or runtime evidence exists.'
    }
    'verify' {
        Invoke-VerifiedTestSuites
        throw 'Full ecosystem verification is blocked. See docs/MASTER-MIGRATION-STATUS.md for missing CUI, platform, packaging, and runtime evidence.'
    }
}
