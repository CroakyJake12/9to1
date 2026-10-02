$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Actual Windows required.' }
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType=WindowsRuntime]
$voices = @([Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices)
if ($voices.Count -lt 1) { throw 'No actual installed Windows voice; native owning lane must fail, never skip.' }
$rows = @($voices | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($_.Id)) { throw 'Actual voice has no stable ID.' }
    [ordered]@{ id = $_.Id; displayName = $_.DisplayName; language = $_.Language; gender = [string]$_.Gender }
})
[ordered]@{
    code = 'ActualWindowsInstalledVoiceInventory'; os = [Environment]::OSVersion.VersionString
    process64Bit = [Environment]::Is64BitProcess; voices = $rows
    playbackStarted = $false; audibleOutputAccepted = $false; sourceAuthorized = $false
} | ConvertTo-Json -Depth 8
