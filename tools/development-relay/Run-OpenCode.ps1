param([Parameter(Mandatory=$true)][string]$Config,[Parameter(Mandatory=$true)][string]$Worktree,[Parameter(Mandatory=$true)][string]$Prompt,[Parameter(Mandatory=$true)][int]$Fence,[Parameter(Mandatory=$true)][string]$TaskId,[Parameter(Mandatory=$true)][string]$OwnedPaths)
$ErrorActionPreference='Stop'
$settings=Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
& node (Join-Path $PSScriptRoot 'relayctl.mjs') check-task $Config $Fence $TaskId
if($LASTEXITCODE -ne 0){throw 'OpenCode startup blocked by schedule or exclusive task ownership.'}
$env:OPENCODE_CONFIG=$settings.opencode_config
$inline=Get-Content -LiteralPath $settings.opencode_config -Raw | ConvertFrom-Json
$inline.permission.bash='deny'
$editRules=[ordered]@{'*'='deny'}
foreach($owned in ($OwnedPaths | ConvertFrom-Json)){
    $relative=([string]$owned).TrimEnd('/')
    $editRules[$relative]='allow'
    $editRules[$relative+'/*']='allow'
    $absolute=(Join-Path $Worktree $relative).Replace('\','/')
    $editRules[$absolute]='allow'
    $editRules[$absolute+'/*']='allow'
}
$inline.permission.edit=$editRules
$env:OPENCODE_CONFIG_CONTENT=$inline | ConvertTo-Json -Depth 20 -Compress
$env:OPENCODE_DISABLE_AUTOUPDATE='true'
Set-Location -LiteralPath $Worktree
& $settings.opencode run --pure --model 'strata/iq2_xs' --format json --dir $Worktree $Prompt
exit $LASTEXITCODE
