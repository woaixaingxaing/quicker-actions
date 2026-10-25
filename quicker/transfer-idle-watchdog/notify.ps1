param(
    [string]$Title = '传输空闲清理',
    [string]$Text = '',
    [int]$Seconds = 8
)

$ErrorActionPreference = 'Stop'

try {
    Add-Type -AssemblyName PresentationFramework
    Add-Type -AssemblyName PresentationCore
    Add-Type -AssemblyName WindowsBase

    [xml]$cardXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="TransferIdleNotice"
        Width="452" Height="160"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" Topmost="True" ResizeMode="NoResize"
        WindowStartupLocation="Manual" Focusable="True" ShowActivated="True">
  <Border Margin="14" CornerRadius="14" Background="#F7F8FCF4">
    <Border.Effect>
      <DropShadowEffect BlurRadius="12" ShadowDepth="2" Direction="270" Opacity="0.30" Color="#FF1F2D46"/>
    </Border.Effect>
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="5"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>
      <Border Grid.Column="0" CornerRadius="14,0,0,14" Background="#FF2F6FED"/>
      <StackPanel Grid.Column="1" Margin="18,15,20,13">
        <TextBlock x:Name="lblTitle" FontFamily="Microsoft YaHei UI" FontWeight="Bold" FontSize="16.5"
                   Foreground="#FF1F2D46" TextTrimming="CharacterEllipsis"/>
        <TextBlock x:Name="lblText" Margin="0,9,0,0" FontFamily="Microsoft YaHei UI" FontSize="13.5"
                   Foreground="#FF525F72" TextWrapping="Wrap" LineHeight="20"/>
      </StackPanel>
    </Grid>
  </Border>
</Window>
'@

    $reader = New-Object System.Xml.XmlNodeReader $cardXaml
    $window = [System.Windows.Markup.XamlReader]::Load($reader)

    $window.FindName('lblTitle').Text = $Title
    $window.FindName('lblText').Text = $Text

    $work = [System.Windows.SystemParameters]::WorkArea
    $window.Left = $work.Right - $window.Width - 12
    $window.Top = $work.Bottom - $window.Height - 12

    $timer = New-Object System.Windows.Threading.DispatcherTimer
    $timer.Interval = [TimeSpan]::FromSeconds([Math]::Max(2, $Seconds))
    $timer.Add_Tick({ $window.Close() })

    $window.Add_MouseLeftButtonDown({ $window.Close() })
    $window.Add_KeyDown({
        if ($_.Key -eq [System.Windows.Input.Key]::Escape) { $window.Close() }
    })
    $window.Add_ContentRendered({
        $window.Activate()
        $window.Focus()
        $timer.Start()
    })
    $window.Add_Closed({ $timer.Stop() })

    $app = New-Object System.Windows.Application
    $app.Run($window) | Out-Null
} catch {
    try {
        $logPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'watchdog.log'
        $message = '[{0}] 通知窗口失败: {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $_.Exception.Message
        Add-Content -LiteralPath $logPath -Value $message -Encoding UTF8
    } catch {
    }
}
