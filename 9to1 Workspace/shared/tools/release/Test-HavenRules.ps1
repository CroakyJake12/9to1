$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$errors = New-Object 'System.Collections.Generic.List[string]'

# Standing requirements consolidated 2026-10-14. The former per-topic *_RULES.md set was
# removed as a conflicting authority, but its still-current obligations are retained there.
$requiredFiles = @(
    'docs\CURRENT-REQUIREMENTS.md'
)
foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add("Missing mandatory requirements file: $relativePath")
    }
}

# CUI-PRO-202 / CURRENT-REQUIREMENTS: Montserrat throughout ordinary UI, with substantial
# weights. These contracts are current and remain enforced here.
if ($errors.Count -eq 0) {
    $defaultThemePath = Join-Path $repositoryRoot 'src\Haven.Desktop\Styles\DefaultTheme.axaml'
    if (-not (Test-Path -LiteralPath $defaultThemePath -PathType Leaf)) {
        $errors.Add('Missing mandatory typography owner: src/Haven.Desktop/Styles/DefaultTheme.axaml')
    } else {
        $defaultTheme = Get-Content -LiteralPath $defaultThemePath -Raw
        foreach ($requiredTypographyContract in @(
            'avares://Haven/Assets/Fonts/MontserratStatic#Montserrat',
            '<Style Selector="TemplatedControl">',
            '<Setter Property="FontWeight" Value="SemiBold" />',
            '<Style Selector="Button">'
        )) {
            if ($defaultTheme -notmatch [regex]::Escape($requiredTypographyContract)) {
                $errors.Add("DefaultTheme.axaml is missing typography contract: $requiredTypographyContract")
            }
        }
    }

    # Light/Thin/ExtraLight UI weights are forbidden across product source.
    $forbiddenFontWeightHits = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Recurse -File |
        Where-Object { $_.Extension -in @('.cs', '.axaml') -and $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -Pattern 'FontWeight\s*=\s*"(?:Thin|ExtraLight|Light)"|FontWeight\.(?:Thin|ExtraLight|Light)' -CaseSensitive:$false
    if ($forbiddenFontWeightHits) {
        foreach ($hit in $forbiddenFontWeightHits) {
            $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $hit.Path)
            $errors.Add("Forbidden light UI font weight at ${relative}:$($hit.LineNumber)")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "Standing requirements, Montserrat typography contract and UI weight rules are valid."