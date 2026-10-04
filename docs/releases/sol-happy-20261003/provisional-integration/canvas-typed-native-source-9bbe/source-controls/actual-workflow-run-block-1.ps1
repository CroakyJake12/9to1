$ErrorActionPreference = 'Stop'
$c = Get-Content -Raw .github/validation/canvas-packaged-ui-controls.json | ConvertFrom-Json
if ($env:GITHUB_REPOSITORY -cne $c.repository) { throw 'Wrong repository.' }
$headers = @{ Authorization = "Bearer $env:GITHUB_TOKEN"; Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
$base = "https://api.github.com/repos/$($c.repository)/actions"
foreach ($expected in $c.artifacts) {
  $run = Invoke-RestMethod -Headers $headers -Uri "$base/runs/$($expected.runId)"
  if ($run.head_sha -cne $expected.headSha) { throw 'Pinned original run source mismatch.' }
  $a = Invoke-RestMethod -Headers $headers -Uri "$base/artifacts/$($expected.id)"
  if ($a.id -ne $expected.id -or $a.name -cne $expected.name -or $a.size_in_bytes -ne $expected.size_in_bytes -or $a.digest -cne $expected.digest -or $a.expired -or $a.workflow_run.id -ne $expected.runId -or $a.workflow_run.head_sha -cne $expected.headSha) { throw 'Immutable original artifact tuple mismatch or expiry.' }
}
