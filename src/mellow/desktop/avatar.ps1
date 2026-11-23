param([switch]$VerifyStudy, [switch]$VerifyScreen, [switch]$VerifyInteraction, [switch]$Classic, [switch]$HatTrick, [switch]$Verify, [switch]$PoseSheet, [switch]$Offscreen, [string]$Preview)
$ErrorActionPreference = 'Stop'
$env:MELLOW_DESKTOP_DIR = $PSScriptRoot
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase,System.Xaml,System.Web.Extensions,System.Windows.Forms,System.Drawing,UIAutomationClient,UIAutomationTypes
$references = @(
    [System.Windows.Window].Assembly.Location
    [System.Windows.Media.Media3D.MeshGeometry3D].Assembly.Location
    [System.Windows.Threading.Dispatcher].Assembly.Location
    [System.ComponentModel.TypeConverter].Assembly.Location
    [System.Windows.Forms.NotifyIcon].Assembly.Location
    [System.Drawing.Icon].Assembly.Location
    [System.Windows.Automation.AutomationElement].Assembly.Location
    [System.Windows.Automation.ControlType].Assembly.Location
    [System.Web.Script.Serialization.JavaScriptSerializer].Assembly.Location
    [System.Xaml.XamlReader].Assembly.Location
)
if ($HatTrick) {
    Add-Type -Path @((Join-Path $PSScriptRoot HatTheatre.cs),(Join-Path $PSScriptRoot ClothSleeves.cs)) -ReferencedAssemblies $references
    [MellowDesktop.HatTheatre]::Run($Preview)
    return
}
Add-Type -Path @((Join-Path $PSScriptRoot Ghost.cs),(Join-Path $PSScriptRoot Desktop.cs),(Join-Path $PSScriptRoot Dialogue.cs),(Join-Path $PSScriptRoot Workspace.cs),(Join-Path $PSScriptRoot Awareness.cs),(Join-Path $PSScriptRoot Recognition.cs),(Join-Path $PSScriptRoot WorkspaceRestore.cs),(Join-Path $PSScriptRoot Study.cs),(Join-Path $PSScriptRoot StudyDesk.cs),(Join-Path $PSScriptRoot Motion.cs),(Join-Path $PSScriptRoot EdgeGrip.cs),(Join-Path $PSScriptRoot GripVerification.cs),(Join-Path $PSScriptRoot PoseReview.cs),(Join-Path $PSScriptRoot Character.cs),(Join-Path $PSScriptRoot Terrain.cs),(Join-Path $PSScriptRoot Perch.cs),(Join-Path $PSScriptRoot PerchVerification.cs),(Join-Path $PSScriptRoot RegionVision.cs),(Join-Path $PSScriptRoot Checkpoint.cs),(Join-Path $PSScriptRoot ClothSleeves.cs),(Join-Path $PSScriptRoot HatTheatre.cs)) -ReferencedAssemblies $references
if ($VerifyStudy) {
    [MellowDesktop.Ghost]::VerifyStudy($Preview)
    return
}
if ($VerifyScreen) {
    [MellowDesktop.Ghost]::VerifyScreen()
    return
}
if ($VerifyInteraction) {
    [MellowDesktop.Ghost]::VerifyDialogue()
    return
}
if ($Offscreen) {
    [MellowDesktop.Ghost]::ReviewRoute($Preview, $Classic.IsPresent)
    return
}
if ($PoseSheet) {
    [MellowDesktop.Ghost]::PoseReview($Preview, $Classic.IsPresent)
    return
}
$closeSignal = $null
if ($env:MELLOW_CLOSE_EVENT) {
    $closeSignal = [Threading.EventWaitHandle]::OpenExisting($env:MELLOW_CLOSE_EVENT)
}
[MellowDesktop.Ghost]::Run($Verify.IsPresent, $Preview, $closeSignal, $Classic.IsPresent)

