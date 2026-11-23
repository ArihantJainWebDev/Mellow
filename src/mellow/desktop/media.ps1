# One local Windows media query or user-directed control. Return a framed result.
param([ValidateSet('current','inspect','play','pause','next')][string]$Action='current')
$ErrorActionPreference = 'Stop'
function Finish-Media($value) {
    $json = ConvertTo-Json -Compress -InputObject $value
    [Console]::WriteLine([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json)))
}
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager,Windows.Media.Control,ContentType=WindowsRuntime]
    $metadataType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties,Windows.Media.Control,ContentType=WindowsRuntime]
    $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq 1 -and
        $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq ('IAsyncOperation' + [char]96 + '1')
    } | Select-Object -First 1
    function Await-Result($operation,[Type]$type) {
        $task = $asTask.MakeGenericMethod($type).Invoke($null,@($operation))
        if (!$task.Wait(3500)) { throw 'Media request timed out.' }
        $task.Result
    }
    $manager = Await-Result ($managerType::RequestAsync()) $managerType
    $encoded = [Console]::ReadLine()
    $source = if ($encoded) {[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded))} else {''}
    if ($Action -eq 'current') {
        $session = $manager.GetCurrentSession()
        if ($null -eq $session) {
            Finish-Media @{available=$false;message='Windows is not reporting a current media session.'}
            exit 0
        }
        $source = $session.SourceAppUserModelId
    }
    $matchingSessions = @($manager.GetSessions() | Where-Object { $_.SourceAppUserModelId -ceq $source })
    $unique = [bool]($source -and $matchingSessions.Count -eq 1)
    if ($Action -ne 'current') {
        if (!$unique) {
            Finish-Media @{available=$false;message='No unique Windows media session for that player.'}
            exit 0
        }
        $session = $matchingSessions[0]
    }
    $properties = Await-Result ($session.TryGetMediaPropertiesAsync()) $metadataType
    $expectedLine=[Console]::ReadLine()
    $expectedArtistLine=[Console]::ReadLine()
    if ($expectedLine) {
        $expected=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($expectedLine))
        $wrongArtist=$expectedArtistLine -and $properties.Artist -cne [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($expectedArtistLine))
        $pausedSkip=$Action -eq 'next' -and $session.GetPlaybackInfo().PlaybackStatus.ToString() -ne 'Playing'
        if ($properties.Title -cne $expected -or $wrongArtist -or $pausedSkip) {
            Finish-Media @{available=$false;message='The track changed before this request.'}
            exit 0
        }
    }
    $changed = $false
    if ($Action -eq 'play') { $changed = Await-Result ($session.TryPlayAsync()) ([bool]) }
    if ($Action -eq 'pause') { $changed = Await-Result ($session.TryPauseAsync()) ([bool]) }
    if ($Action -eq 'next') { $changed = Await-Result ($session.TrySkipNextAsync()) ([bool]) }
    Finish-Media @{available=$true;unique=$unique;source=$source;title=$properties.Title;artist=$properties.Artist;
        playing=($session.GetPlaybackInfo().PlaybackStatus.ToString() -eq 'Playing');changed=$changed}
} catch {
    Finish-Media @{available=$false;message='Windows media integration is unavailable.'}
}
