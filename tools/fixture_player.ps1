# Exposes a silent, locally generated track to Windows for the media bridge smoke check.
param([string]$Wave,[string]$Track,[string]$StopFile)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
$sourceType=[Windows.Media.Core.MediaSource,Windows.Media,ContentType=WindowsRuntime]
$itemType=[Windows.Media.Playback.MediaPlaybackItem,Windows.Media,ContentType=WindowsRuntime]
$playerType=[Windows.Media.Playback.MediaPlayer,Windows.Media,ContentType=WindowsRuntime]
$source=$sourceType::CreateFromUri([Uri]::new($Wave))
$item=$itemType::new($source)
$properties=$item.GetDisplayProperties()
$properties.Type='Music'
$properties.MusicProperties.Title=$Track
$properties.MusicProperties.Artist='Mellow fixture'
$item.ApplyDisplayProperties($properties)
$player=$playerType::new()
$player.Volume=0
$player.IsLoopingEnabled=$true
$player.Source=$item
try {
    $player.Play()
    [Console]::WriteLine('READY')
    $limit=[DateTime]::UtcNow.AddSeconds(75)
    while (!(Test-Path -LiteralPath $StopFile) -and [DateTime]::UtcNow -lt $limit) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 60
    }
} finally {
    $player.Pause()
    $player.Source=$null
}
