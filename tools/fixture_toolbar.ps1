param([switch]$Awareness)
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
if ($Awareness) {
    $w.Title = 'Linear algebra quiz - Awareness verification'
    $w.Left = [Windows.SystemParameters]::WorkArea.Left + 40
    $w.Top = [Windows.SystemParameters]::WorkArea.Top + 180
    $w.Width = 680
    $w.Height = 250
    $w.ShowActivated = $true
    $w.ShowInTaskbar = $true
    $w.Topmost = $true
    $editor = New-Object Windows.Controls.TextBox
    $editor.Text = "Linear algebra practice quiz: BLUEBIRD`nStudying eigenvectors and matrices"
    $editor.FontSize = 26
    $editor.AcceptsReturn = $true
    $editor.IsReadOnly = $true
    $w.Content = $editor
    $w.Add_Activated({ $null = $editor.Focus() })
    $protected = New-Object Windows.Window
    $protected.Title = 'Mellow protected focus fixture'
    $protected.WindowStartupLocation = 'Manual'
    $protected.Left = $w.Left + 80
    $protected.Top = $w.Top + 260
    $protected.Width = 300
    $protected.Height = 100
    $protected.ShowActivated = $false
    $protected.ShowInTaskbar = $false
    $protected.Content = New-Object Windows.Controls.PasswordBox
    $protected.Add_Activated({ $null = $protected.Content.Focus() })
    $w.Add_Closed({ $protected.Close() })
    $w.Add_SourceInitialized({
        $handle = (New-Object Windows.Interop.WindowInteropHelper $w).Handle
        $script:focusHook = [Windows.Interop.HwndSourceHook] {
            param($hwnd, $message, $wparam, $lparam, $handled)
            if ($message -eq 0x8001 -and $wparam.ToInt64() -eq 24843) {
                $null = $protected.Activate()
                $null = $protected.Content.Focus()
            }
            [IntPtr]::Zero
        }
        [Windows.Interop.HwndSource]::FromHwnd($handle).AddHook($script:focusHook)
    })
    $w.Add_ContentRendered({
        $protected.Show()
        $null = $w.Activate()
        $null = $editor.Focus()
        [Console]::WriteLine((New-Object Windows.Interop.WindowInteropHelper $w).Handle.ToInt64())
        [Console]::WriteLine((New-Object Windows.Interop.WindowInteropHelper $protected).Handle.ToInt64())
        [Console]::Out.Flush()
    })
} else { $w.Add_ContentRendered({
    [Console]::WriteLine((New-Object Windows.Interop.WindowInteropHelper $w).Handle.ToInt64())
    [Console]::Out.Flush()
}) }
$app = New-Object Windows.Application
$null = $app.Run($w)
