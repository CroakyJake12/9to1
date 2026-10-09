$ErrorActionPreference = 'Stop'
$source = '/workspace/team-c-c3-boards-storage-retry/.github/scripts/boards-packaged-ui-controls.ps1'
$tokens=$null; $errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count -ne 0){throw 'Actual source parser errors.'}
$definition=$ast.FindAll({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Observe-SaveLabel'},$true)[0]
Invoke-Expression $definition.Extent.Text
function Wait-Observed { throw 'Timed out observing: Visible native save text: Saved' }
function Write-Result { $script:writeCount++ }
$script:writeCount=0
$process=[pscustomobject]@{HasExited=$true}
$result=[ordered]@{}
$caught=$null
try { Observe-SaveLabel 'Saved' $false } catch { $caught=$_ }
if($null -eq $caught -or $caught.Exception.Message -cne 'Timed out observing: Visible native save text: Saved' -or $caught.Exception.GetType().FullName -cne 'System.Management.Automation.RuntimeException'){throw 'Original lookup refusal was not preserved.'}
if($result.storageStatusDiagnosticFailure.message -cne 'Status diagnostic requires the exact live app window.' -or $writeCount -ne 1){throw 'Diagnostic refusal did not remain independent.'}
[ordered]@{
    status='PASS'; actualSourceSha256=(Get-FileHash $source -Algorithm SHA256).Hash.ToLower()
    actualFunction='Observe-SaveLabel'; parserErrors=$errors.Count
    controlledLookupRefusalType=$caught.Exception.GetType().FullName; controlledLookupRefusalMessage=$caught.Exception.Message
    diagnosticCapabilityRefusal=$result.storageStatusDiagnosticFailure.message
    qualification='Actual frozen-function diagnostic refusal/rethrow boundary only. Controlled process and lookup refusal; no Windows, UIA, physical files, app/provider/model or live inputs.'
} | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8NoBOM '/workspace/team-c-resume-evidence/c3-boards-storage-retry-20261004/diagnostic-continuation/actual-refusal-rethrow-receipt.json'
Write-Output 'Actual new Observe-SaveLabel retains original timeout type/message despite diagnostic refusal: PASS'
