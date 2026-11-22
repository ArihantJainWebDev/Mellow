param([switch]$Verify, [string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,PresentationFramework,WindowsBase,System.Xaml
$references = @(
    [System.Windows.Window].Assembly.Location
    [System.Windows.Media.Media3D.MeshGeometry3D].Assembly.Location
    [System.Windows.Threading.Dispatcher].Assembly.Location
    [System.ComponentModel.TypeConverter].Assembly.Location
    [System.Xaml.XamlReader].Assembly.Location
)
Add-Type -Path (Join-Path $PSScriptRoot Ghost.cs) -ReferencedAssemblies $references
[MellowDesktop.Ghost]::Run($Verify.IsPresent, $Preview)

