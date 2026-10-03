$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Actual Windows required.' }
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType=WindowsRuntime]
$voices = @([Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices)
if ($voices.Count -lt 1) { throw 'No actual installed Windows voice; native owning lane must fail, never skip.' }
$rows = @($voices | ForEach-Object {
    if ([string]::IsNullOrWhiteSpace($_.Id)) { throw 'Actual voice has no stable ID.' }
    [ordered]@{ id = $_.Id; displayName = $_.DisplayName; language = $_.Language; gender = [string]$_.Gender }
})
# Read actual host output prerequisites. Do not start services, install a device,
# change endpoint selection or infer playback acceptance from this inventory.
$soundDevices = @(Get-CimInstance -ClassName Win32_SoundDevice -ErrorAction Stop | ForEach-Object {
    [ordered]@{ name = $_.Name; status = $_.Status; configurationError = $_.ConfigManagerErrorCode }
})
$audioServices = @(Get-Service -Name Audiosrv, AudioEndpointBuilder -ErrorAction Stop | ForEach-Object {
    [ordered]@{ name = $_.Name; status = [string]$_.Status }
})
$renderRoot = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render'
$renderRootPresent = Test-Path -LiteralPath $renderRoot
$renderEndpoints = @()
if ($renderRootPresent) {
    $renderEndpoints = @(Get-ChildItem -LiteralPath $renderRoot -ErrorAction Stop | ForEach-Object {
        $endpoint = Get-ItemProperty -LiteralPath $_.PSPath -Name DeviceState -ErrorAction Stop
        [ordered]@{ id = $_.PSChildName; deviceState = [uint32]$endpoint.DeviceState }
    })
}
[ordered]@{
    code = 'ActualWindowsInstalledVoiceInventory'; os = [Environment]::OSVersion.VersionString
    process64Bit = [Environment]::Is64BitProcess; voices = $rows
    audioServices = $audioServices; soundDevices = $soundDevices
    renderRegistryPresent = $renderRootPresent; renderEndpoints = $renderEndpoints
    playbackStarted = $false; audibleOutputAccepted = $false; sourceAuthorized = $false
} | ConvertTo-Json -Depth 8
