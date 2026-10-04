$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$output=[IO.Path]::GetFullPath($env:PROBE_OUTPUT)
if(Test-Path -LiteralPath $output){throw 'Evidence directory must be new.'}
[void][IO.Directory]::CreateDirectory($output)
$source='.github/scripts/canvas-packaged-ui-controls.ps1'
$result=[ordered]@{schemaVersion=1;status='NOT_RUN';stage='installed-standard-uia-provider-diagnostic';workflowCommit='${{ github.sha }}';sourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant();checks=@();launches=@();requests=@();canvasStates=@();forcedCleanup=$false;acceptanceVerified=$false;fullAppAcceptance='NOT_RUN';installedHomeTuple='NOT_RUN';cleanPcDelivery='NOT_RUN';failure=$null;qualification='Setup-only SDK-present hosted Windows client diagnostic. Exact genuine installed Microsoft GAC provider/public API; bounded exception type/HRESULT and canonical public type/table capability only. No package download/extraction, native app/process/input/picker/profile/fixture/provider resource/model/approval state.'}
$tokens=$null;$errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $source).Path,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'Actual source parser errors.'}
foreach($name in @('Write-Result','Check','Test-GenuineUiAutomationAssembly','Read-UiAutomationAssembly','Read-BoundedProviderExceptionTypes','Read-KnownProviderThrowFrames','Record-StandardProviderRefusalWitness','Initialize-StandardUiAutomationProviders')){
  $definitions=@($ast.FindAll({param($node)$node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name},$true))
  if($definitions.Count -ne 1){throw 'Exact source function is absent or ambiguous.'}
  Invoke-Expression $definitions[0].Extent.Text
}
$exitCode=1
try{
  $catalog=Get-Content -Raw .github/validation/canvas-packaged-ui-controls.json|ConvertFrom-Json
  foreach($pin in $catalog.sourcePins){Check ((Get-FileHash -LiteralPath $pin.path -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $pin.sha256) "Exact unchanged native source pin: $($pin.path)"}
  Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
  Initialize-StandardUiAutomationProviders
  $result.status='SETUP_ONLY_COMPLETED_NATIVE_WORKFLOW_NOT_RUN';$exitCode=0
}catch{
  $result.status='ORIGINAL_PROVIDER_SETUP_REFUSAL_RETAINED'
  $result.failure=Read-BoundedProviderExceptionTypes $_.Exception
}finally{Write-Result}
exit $exitCode
