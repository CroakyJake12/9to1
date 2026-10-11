param(
    [Parameter(Mandatory=$true)][string]$DonorSource,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$VisualStudio = 'C:/Program Files/Microsoft Visual Studio/18/Community'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $DonorSource).Path
$expected = 'd3fca73f81dfb19a72a0da41598b7390eaf5845b'
$commit = & git -c "safe.directory=$source" -C $source rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $commit -ne $expected) { throw 'The donor source must match the pinned LibreOffice commit.' }
& git -c "safe.directory=$source" -C $source diff --exit-code HEAD -- include/LibreOfficeKit
if ($LASTEXITCODE -ne 0) { throw 'The pinned LibreOfficeKit headers have local modifications.' }
$vcvars = Join-Path $VisualStudio 'VC/Auxiliary/Build/vcvars64.bat'
if (!(Test-Path -LiteralPath $vcvars)) { throw 'Install the Visual C++ build workload or pass its existing VisualStudio root.' }
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
[void][System.IO.Directory]::CreateDirectory($output)
$unit = Join-Path $PSScriptRoot '9to1_write_lok.cxx'
$include = Join-Path $source 'include'
foreach ($path in @($vcvars, $unit, $include, $output)) {
    if ($path -match '["&|<>^%\r\n]') { throw 'This build entrypoint requires paths without shell metacharacters.' }
}
$dll = Join-Path $output '9to1-write-lok.dll'
$object = Join-Path $output '9to1-write-lok.obj'
$command = 'call "{0}" >nul && cl.exe /nologo /std:c++20 /EHsc /MD /W4 /LD /I"{1}" "{2}" /Fo"{3}" /link /OUT:"{4}"' -f $vcvars, $include, $unit, $object, $dll
$previousTemp = $env:TEMP; $previousTmp = $env:TMP
try {
    $env:TEMP = $output; $env:TMP = $output
    Push-Location -LiteralPath $output
    try { & $env:ComSpec /d /s /c $command; $compileExit = $LASTEXITCODE }
    finally { Pop-Location }
    if ($compileExit -ne 0) { throw "Native LOK bridge compile failed: $compileExit" }
}
finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
$manifest = [ordered]@{
    component = '9to1 Write LOK native ABI bridge'; abi = 1; donorCommit = $commit
    artifact = $dll; sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    compiledAgainstPinnedHeaders = $true; donorRuntimeExecution = 'NOT RUN'
    shippingIntegration = 'NOT RUN'; donorParityAcceptance = 'NOT RUN'
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'build-evidence.json') -Encoding utf8
$manifest | ConvertTo-Json
