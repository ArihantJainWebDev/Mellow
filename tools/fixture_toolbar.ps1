# A local accessibility fixture. Never reads an existing browser window.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
$w = New-Object Windows.Window
$w.Title = 'Mellow study verification fixture'
$w.WindowStartupLocation = 'Manual'
$w.Left = [Windows.SystemParameters]::VirtualScreenLeft - 550
$w.Top = [Windows.SystemParameters]::VirtualScreenTop
$w.Width = 430
$w.Height = 160
$w.ShowActivated = $false
$w.ShowInTaskbar = $false
$panel = New-Object Windows.Controls.StackPanel
$outside = New-Object Windows.Controls.TextBox
$outside.Text = 'https://example.org/not-a-toolbar'
[Windows.Automation.AutomationProperties]::SetName($outside,'Address and search bar')
$null = $panel.Children.Add($outside)
$bar = New-Object Windows.Controls.ToolBar
$field = New-Object Windows.Controls.TextBox
$field.Width = 340
$field.Text = 'https://example.org/course'
[Windows.Automation.AutomationProperties]::SetName($field,'Address and search bar')
$null = $bar.Items.Add($field)
$null = $panel.Children.Add($bar)
$w.Content = $panel
$w.Add_ContentRendered({
    [Console]::WriteLine((New-Object Windows.Interop.WindowInteropHelper $w).Handle.ToInt64())
    [Console]::Out.Flush()
})
$app = New-Object Windows.Application
$null = $app.Run($w)
