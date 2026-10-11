param([Parameter(Mandatory=$true)][string]$KeyPath)
$ErrorActionPreference='Stop'
if([IO.Path]::GetFileName($KeyPath) -ne 'local-signing.pem'){throw 'Only the newly generated relay signing key may be protected.'}
Add-Type -AssemblyName System.Security
$plain=[IO.File]::ReadAllBytes($KeyPath)
try{
    $cipher=[Security.Cryptography.ProtectedData]::Protect($plain,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path (Split-Path -Parent $KeyPath) 'local-signing.dpapi'),$cipher)
}finally{[Array]::Clear($plain,0,$plain.Length)}
Remove-Item -LiteralPath $KeyPath
Write-Output 'Relay signing key protected for the current Windows user.'
