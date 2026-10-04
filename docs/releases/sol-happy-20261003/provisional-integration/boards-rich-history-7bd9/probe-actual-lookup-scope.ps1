$ErrorActionPreference = 'Stop'
$originalPath = $args[0]
$correctedPath = $args[1]
$receiptPath = $args[2]
function Function-Body([string]$Path, [string]$FunctionName) {
    $tokens = $null; $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) { throw 'Actual source has parser errors.' }
    return $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $FunctionName }, $true).Extent.Text
}
$records = @()
foreach ($case in @(@{ label = 'original-9c3aa3d'; path = $originalPath; expected = 'Actual named native button: Bold' }, @{ label = 'corrected'; path = $correctedPath; expected = 'Bold' })) {
    Invoke-Expression (Function-Body $case.path 'Wait-Observed')
    Invoke-Expression (Function-Body $case.path 'Observe-NamedButton')
    # Controlled dependency throws at the first lookup, before any UIA control
    # observation or Check. This diagnoses actual PowerShell scope only.
    function Find-NamedButton($Window, [string]$Name) { $script:receivedLookupName = $Name; throw 'ControlledFirstLookupStop' }
    $window = $null; $script:receivedLookupName = $null
    try { Observe-NamedButton 'Bold'; throw 'Expected first-lookup stop.' }
    catch { if ($_.Exception.Message -cne 'ControlledFirstLookupStop') { throw } }
    if ($script:receivedLookupName -cne $case.expected) { throw 'Actual source lookup scope differs from expected control.' }
    $records += [ordered]@{ case = $case.label; actualSource = $case.path; requestedNativeName = 'Bold'; receivedLookupName = $script:receivedLookupName; matchesRequestedName = $script:receivedLookupName -ceq 'Bold' }
}
[ordered]@{ status = 'ACTUAL_FUNCTION_SCOPE_DEFECT_REPRODUCED_AND_CORRECTED'; controls = $records; qualification = 'Exact actual Observe-NamedButton and Wait-Observed bodies invoked with controlled throwing lookup boundary. Scope diagnostic only; no UIA/window/app/mock-native acceptance or provider operations.' } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
