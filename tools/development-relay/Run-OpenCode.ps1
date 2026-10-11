param([Parameter(Mandatory=$true)][string]$Config,[Parameter(Mandatory=$true)][string]$Worktree,[Parameter(Mandatory=$true)][string]$Prompt,[Parameter(Mandatory=$true)][int]$Fence,[Parameter(Mandatory=$true)][string]$TaskId,[Parameter(Mandatory=$true)][string]$OwnedPaths,[string]$SessionId)
$ErrorActionPreference='Stop'
$settings=Get-Content -LiteralPath $Config -Raw | ConvertFrom-Json
& node (Join-Path $PSScriptRoot 'relayctl.mjs') check-task $Config $Fence $TaskId
if($LASTEXITCODE -ne 0){throw 'OpenCode startup blocked by schedule or exclusive task ownership.'}
$env:OPENCODE_CONFIG=$settings.opencode_config
# Keep relay sessions separate from the owner's existing OpenCode database/config.
$stateRoot=Join-Path (Split-Path -Parent $Config) 'opencode-isolated'
foreach($kind in @('data','cache','state','config')){New-Item -ItemType Directory -Path (Join-Path $stateRoot $kind) -Force | Out-Null}
$env:XDG_DATA_HOME=Join-Path $stateRoot 'data'
$env:XDG_CACHE_HOME=Join-Path $stateRoot 'cache'
$env:XDG_STATE_HOME=Join-Path $stateRoot 'state'
$env:XDG_CONFIG_HOME=Join-Path $stateRoot 'config'
$inline=Get-Content -LiteralPath $settings.opencode_config -Raw | ConvertFrom-Json
$inline | Add-Member -NotePropertyName enabled_providers -NotePropertyValue @('strata') -Force
$inline | Add-Member -NotePropertyName agent -NotePropertyValue @{relay=@{mode='primary';model='strata/iq2_xs';description='Bounded owned local relay worker'}} -Force
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
$runArgs=@('run','--pure','--print-logs','--log-level','INFO','--agent','relay','--model','strata/iq2_xs','--format','json','--dir',$Worktree)
if($SessionId){if($SessionId -notmatch '^ses_[a-zA-Z0-9]{8,80}$'){throw 'Known OpenCode session required'};$runArgs+=@('--session',$SessionId)}
# OpenCode1.17.15 reads piped stdin. Diagnostic JSON/quotes/flags must remain
# prompt text rather than being reparsed as Windows command-line arguments.
$previousOutputEncoding=$OutputEncoding
try {
    $OutputEncoding=New-Object System.Text.UTF8Encoding($false)
    $Prompt | & $settings.opencode @runArgs
    $workerExitCode=$LASTEXITCODE
} finally {
    $OutputEncoding=$previousOutputEncoding
}
exit $workerExitCode
