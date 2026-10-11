param([Parameter(Mandatory=$true)][string]$PrivateDirectory)
$ErrorActionPreference='Stop'
$config=Join-Path $env:USERPROFILE '.codex\config.toml'
$backup=Join-Path $PrivateDirectory ('codex-config-before-strata-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.toml')
$raw=[IO.File]::ReadAllText($config)
if($raw -match '(?m)^\[profiles\.("strata"|''strata''|strata)\]') { Write-Output 'Existing strata profile preserved; validate its settings before changing it.';exit 0 }
if($raw -match '(?m)^\[model_providers\.("strata"|''strata''|strata)\]') {throw 'Existing provider without profile needs reconciliation; left unchanged.'}
Copy-Item -LiteralPath $config -Destination $backup
$append=@'

[model_providers.strata]
name = "Strata loopback"
base_url = "http://127.0.0.1:8081/v1"
wire_api = "responses"
requires_openai_auth = false

[profiles.strata]
model = "qwen3.8-flash-next-iq2_xs"
model_provider = "strata"
model_context_window = 524288
model_auto_compact_token_limit = 450560
'@
[IO.File]::WriteAllText($config,$raw+$append,[Text.UTF8Encoding]::new($false))
Write-Output 'Added only the missing named Strata provider/profile. Other settings and default model preserved; private backup created.'
