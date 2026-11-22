param([switch]$HatTrick, [switch]$Verify, [string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase,System.Xaml,System.Windows.Forms,System.Drawing,UIAutomationClient,UIAutomationTypes
$references = @(
    [System.Windows.Window].Assembly.Location
    [System.Windows.Media.Media3D.MeshGeometry3D].Assembly.Location
    [System.Windows.Threading.Dispatcher].Assembly.Location
    [System.ComponentModel.TypeConverter].Assembly.Location
    [System.Windows.Forms.NotifyIcon].Assembly.Location
    [System.Drawing.Icon].Assembly.Location
    [System.Windows.Automation.AutomationElement].Assembly.Location
    [System.Windows.Automation.ControlType].Assembly.Location
    [System.Xaml.XamlReader].Assembly.Location
)
if ($HatTrick) {
    Add-Type -Path (Join-Path $PSScriptRoot HatTheatre.cs) -ReferencedAssemblies $references
    [MellowDesktop.HatTheatre]::Run($Preview)
    return
}
Add-Type -Path @((Join-Path $PSScriptRoot Ghost.cs),(Join-Path $PSScriptRoot Desktop.cs),(Join-Path $PSScriptRoot Motion.cs),(Join-Path $PSScriptRoot Character.cs)) -ReferencedAssemblies $references
$closeSignal = $null
if ($env:MELLOW_CLOSE_EVENT) {
    $closeSignal = [Threading.EventWaitHandle]::OpenExisting($env:MELLOW_CLOSE_EVENT)
}
[MellowDesktop.Ghost]::Run($Verify.IsPresent, $Preview, $closeSignal)

