$ErrorActionPreference = 'Stop'
$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($args[0], [ref]$tokens, [ref]$errors)
$result = [ordered]@{ parserVersion = $PSVersionTable.PSVersion.ToString(); parsedActualSource = $args[0]; errors = @($errors | ForEach-Object { [ordered]@{ errorId = $_.ErrorId; message = $_.Message; line = $_.Extent.StartLineNumber } }); qualification = 'Linux PowerShell7 syntax parsing only, not Windows PowerShell5.1/native UI execution.' }
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $args[1] -Encoding UTF8
if ($errors.Count -ne 0) { exit 1 }
exit 0
