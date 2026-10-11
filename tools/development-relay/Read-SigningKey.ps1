param([Parameter(Mandatory=$true)][string]$KeyPath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
$cipher=[IO.File]::ReadAllBytes($KeyPath)
$plain=[Security.Cryptography.ProtectedData]::Unprotect($cipher,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
try {[Console]::Out.Write([Text.Encoding]::UTF8.GetString($plain))}finally{[Array]::Clear($plain,0,$plain.Length)}
