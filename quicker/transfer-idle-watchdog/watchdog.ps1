# transfer-idle-watchdog v1.0.1
param(
    [Parameter(Mandatory = $true)][string]$DataDir,
    [Parameter(Mandatory = $true)][string]$Token,
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
$script:StartedAt = (Get-Date).ToString('o')
$configPath = Join-Path $DataDir 'apps.json'
$statusPath = Join-Path $DataDir 'status.json'
$logPath = Join-Path $DataDir 'watchdog.log'
$stopPath = Join-Path $DataDir 'stop.flag'
$script:LastEvent = '已启动'

$ProtectedNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($name in @(
        'system', 'idle', 'registry', 'memory compression', 'secure system',
        'smss', 'csrss', 'wininit', 'winlogon', 'services', 'lsass', 'svchost',
        'dwm', 'fontdrvhost', 'sihost', 'taskhostw', 'ctfmon', 'explorer',
        'conhost', 'powershell', 'pwsh', 'cmd', 'wscript', 'cscript', 'mshta',
        'rundll32', 'regsvr32', 'spoolsv', 'audiodg', 'searchindexer',
        'wuauclt', 'trustedinstaller', 'searchhost', 'startmenuexperiencehost',
        'shellexperiencehost', 'textinputhost', 'applicationframehost', 'lockapp',
        'smartscreen', 'securityhealthservice', 'msmpeng', 'nissrv',
        'backgroundtaskhost', 'systemsettings', 'searchapp', 'runtimebroker',
        'dllhost', 'taskhost', 'searchprotocolhost', 'searchfilterhost',
        'widgetservice'
    )) {
    [void]$ProtectedNames.Add($name)
}

function Write-Log([string]$message) {
    try {
        $line = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + '  ' + $message
        Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    } catch {
    }
}

function Write-Status($apps, [int]$sampleSeconds) {
    try {
        $obj = [pscustomobject]@{
            version       = $Version
            pid           = $PID
            token         = $Token
            startedAt     = $script:StartedAt
            updatedAt     = (Get-Date).ToString('o')
            sampleSeconds = $sampleSeconds
            apps          = @($apps)
            lastEvent     = $script:LastEvent
        }
        $json = $obj | ConvertTo-Json -Depth 6
        Set-Content -LiteralPath $statusPath -Value $json -Encoding UTF8
    } catch {
        Write-Log ('写入状态失败: ' + $_.Exception.Message)
    }
}

function Show-Balloon([string]$title, [string]$text) {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        Add-Type -AssemblyName System.Drawing
        $icon = New-Object System.Windows.Forms.NotifyIcon
        $icon.Icon = [System.Drawing.SystemIcons]::Information
        $icon.Visible = $true
        $icon.BalloonTipTitle = $title
        $icon.BalloonTipText = $text
        $icon.BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::Info
        $icon.ShowBalloonTip(6000)
        Start-Sleep -Seconds 7
        $icon.Dispose()
    } catch {
        Write-Log ('通知失败: ' + $_.Exception.Message)
    }
}

function ConvertTo-ProcName([string]$name) {
    if ([string]::IsNullOrWhiteSpace($name)) { return '' }
    $name = $name.Trim()
    if ($name.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        $name = $name.Substring(0, $name.Length - 4)
    }
    return $name
}

function Read-WatchConfig {
    $raw = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8
    $cfg = $raw | ConvertFrom-Json
    $notifications = $true
    if ($null -ne $cfg.notifications) { $notifications = [bool]$cfg.notifications }
    $sampleSeconds = 10
    try {
        if ($null -ne $cfg.sampleSeconds) { $sampleSeconds = [int]$cfg.sampleSeconds }
    } catch {
        $sampleSeconds = 10
    }
    if ($sampleSeconds -lt 5) { $sampleSeconds = 5 }
    if ($sampleSeconds -gt 120) { $sampleSeconds = 120 }

    $apps = @()
    foreach ($a in @($cfg.apps)) {
        if ($null -eq $a) { continue }
        if ($a.enabled -eq $false) { continue }

        $procNames = @()
        foreach ($n in @($a.processes)) {
            $n = ConvertTo-ProcName ([string]$n)
            if ($n) { $procNames += $n }
        }
        if ($procNames.Count -eq 0) { continue }

        $transferNames = @()
        foreach ($n in @($a.transferProcesses)) {
            $n = ConvertTo-ProcName ([string]$n)
            if ($n) { $transferNames += $n }
        }
        if ($transferNames.Count -eq 0) { $transferNames = $procNames }

        $idleMinutes = 5.0
        try {
            if ($null -ne $a.idleMinutes) { $idleMinutes = [double]$a.idleMinutes }
        } catch {
            $idleMinutes = 5.0
        }
        if ($idleMinutes -lt 0.1) { $idleMinutes = 0.1 }
        if ($idleMinutes -gt 1440) { $idleMinutes = 1440 }

        $thresholdKB = 32
        try {
            if ($null -ne $a.thresholdKB) { $thresholdKB = [int]$a.thresholdKB }
        } catch {
            $thresholdKB = 32
        }
        if ($thresholdKB -lt 1) { $thresholdKB = 1 }
        if ($thresholdKB -gt 1048576) { $thresholdKB = 1048576 }

        $apps += [pscustomobject]@{
            name                = [string]$a.name
            processes           = @($procNames)
            transferProcesses   = @($transferNames)
            transferCommandLine = [string]$a.transferCommandLine
            idleSeconds         = [int][math]::Round($idleMinutes * 60)
            idleMinutes         = $idleMinutes
            thresholdBytes      = [long]$thresholdKB * 1024
        }
    }

    return [pscustomobject]@{
        sampleSeconds = $sampleSeconds
        notifications = $notifications
        apps          = @($apps)
    }
}

function Get-NameMap($all) {
    $map = @{}
    foreach ($p in $all) {
        $n = ConvertTo-ProcName ([string]$p.Name)
        if (-not $n) { continue }
        if (-not $map.ContainsKey($n)) {
            $map[$n] = New-Object System.Collections.ArrayList
        }
        [void]$map[$n].Add($p)
    }
    return $map
}

function Get-AppProcesses($app, $nameMap) {
    $set = @{}
    foreach ($n in @($app.processes)) {
        if (-not $nameMap.ContainsKey($n)) { continue }
        foreach ($p in @($nameMap[$n])) {
            $set[[int]$p.ProcessId] = $p
        }
    }
    return @($set.Values)
}

function Get-TransferBytes($app, $nameMap) {
    $set = @{}
    foreach ($n in @($app.transferProcesses)) {
        if (-not $nameMap.ContainsKey($n)) { continue }
        foreach ($p in @($nameMap[$n])) {
            $cmd = [string]$p.CommandLine
            if ($app.transferCommandLine -and $cmd.IndexOf([string]$app.transferCommandLine, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
                continue
            }
            $set[[int]$p.ProcessId] = $p
        }
    }
    [long]$sum = 0
    foreach ($p in $set.Values) {
        $sum += [long]$p.ReadTransferCount
        $sum += [long]$p.WriteTransferCount
    }
    return $sum
}

function Test-KillAllowed($proc, [int]$selfId, [string]$windowsPrefix) {
    $procId = [int]$proc.ProcessId
    if ($procId -le 4 -or $procId -eq $selfId) { return $false }
    $name = ConvertTo-ProcName ([string]$proc.Name)
    if (-not $name) { return $false }
    if ($ProtectedNames.Contains($name)) { return $false }
    if ($name.StartsWith('quicker', [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
    $path = [string]$proc.ExecutablePath
    if (-not [string]::IsNullOrWhiteSpace($path)) {
        try {
            $full = [System.IO.Path]::GetFullPath($path)
            if ($full.StartsWith($windowsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { return $false }
        } catch {
            return $false
        }
    }
    return $true
}

function Kill-AppProcesses($app, [int]$selfId, [string]$windowsPrefix, $all) {
    $nameSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($n in @($app.processes)) { [void]$nameSet.Add((ConvertTo-ProcName $n)) }

    $target = @{}
    foreach ($p in $all) {
        $n = ConvertTo-ProcName ([string]$p.Name)
        if ($nameSet.Contains($n)) { $target[[int]$p.ProcessId] = $true }
    }
    if ($target.Count -eq 0) { return [pscustomobject]@{ Killed = 0; Remaining = 0; Names = @() } }

    $changed = $true
    $guard = 0
    while ($changed -and $guard -lt 12) {
        $guard++
        $changed = $false
        foreach ($p in $all) {
            $parent = [int]$p.ParentProcessId
            $procId = [int]$p.ProcessId
            if ($target.ContainsKey($parent) -and -not $target.ContainsKey($procId)) {
                $target[$procId] = $true
                $changed = $true
            }
        }
    }

    $byId = @{}
    foreach ($p in $all) { $byId[[int]$p.ProcessId] = $p }

    $roots = @()
    $children = @()
    $skipped = @()
    foreach ($procId in @($target.Keys)) {
        if (-not $byId.ContainsKey($procId)) { continue }
        $p = $byId[$procId]
        if (-not (Test-KillAllowed $p $selfId $windowsPrefix)) {
            $skipped += (ConvertTo-ProcName ([string]$p.Name))
            continue
        }
        if ($target.ContainsKey([int]$p.ParentProcessId)) { $children += $p } else { $roots += $p }
    }

    $killed = 0
    foreach ($p in @($roots + $children)) {
        try {
            Stop-Process -Id ([int]$p.ProcessId) -Force -ErrorAction Stop
            $killed++
        } catch {
            Write-Log ('结束进程失败 ' + $p.Name + ' (' + $p.ProcessId + '): ' + $_.Exception.Message)
        }
    }

    Start-Sleep -Milliseconds 1200

    try {
        $after = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)
    } catch {
        $after = @()
    }
    $remainingTargets = @{}
    foreach ($p in $after) {
        $n = ConvertTo-ProcName ([string]$p.Name)
        if ($nameSet.Contains($n)) { $remainingTargets[[int]$p.ProcessId] = $true }
    }
    $changed = $true
    $guard = 0
    while ($changed -and $guard -lt 12) {
        $guard++
        $changed = $false
        foreach ($p in $after) {
            $parent = [int]$p.ParentProcessId
            $procId = [int]$p.ProcessId
            if ($remainingTargets.ContainsKey($parent) -and -not $remainingTargets.ContainsKey($procId)) {
                $remainingTargets[$procId] = $true
                $changed = $true
            }
        }
    }

    $remaining = 0
    foreach ($p in $after) {
        if (-not $remainingTargets.ContainsKey([int]$p.ProcessId)) { continue }
        if (-not (Test-KillAllowed $p $selfId $windowsPrefix)) { continue }
        $remaining++
        try {
            Stop-Process -Id ([int]$p.ProcessId) -Force -ErrorAction Stop
            $killed++
        } catch {
        }
    }

    Start-Sleep -Milliseconds 500
    try {
        $final = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)
    } catch {
        $final = @()
    }
    $left = 0
    $leftNames = @()
    foreach ($p in $final) {
        $n = ConvertTo-ProcName ([string]$p.Name)
        if ($nameSet.Contains($n)) {
            $left++
            $leftNames += $n
        }
    }

    return [pscustomobject]@{
        Killed    = $killed
        Remaining = $left
        Names     = @($leftNames | Sort-Object -Unique)
        Skipped   = @($skipped | Sort-Object -Unique)
    }
}

if (Test-Path -LiteralPath $logPath) {
    try {
        if ((Get-Item -LiteralPath $logPath).Length -gt 1048576) {
            Move-Item -LiteralPath $logPath -Destination ($logPath + '.1') -Force
        }
    } catch {
    }
}

$windowsPrefix = [System.IO.Path]::GetFullPath($env:WINDIR).TrimEnd('\') + '\'
Write-Log ('监控启动 pid=' + $PID + ' version=' + $Version)

# 只保留一个监控实例：同时启动时保留 PID 较小的那个。
Start-Sleep -Seconds 1
try {
    $selfPath = [string]$MyInvocation.MyCommand.Path
    if ($selfPath) {
        $others = @(Get-CimInstance -ClassName Win32_Process -Filter "Name='powershell.exe'" -ErrorAction Stop |
            Where-Object {
                $_.ProcessId -ne $PID -and $_.CommandLine -and
                $_.CommandLine.IndexOf($selfPath, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            } |
            ForEach-Object { [int]$_.ProcessId })
        if ($others.Count -gt 0) {
            $lowest = ($others | Measure-Object -Minimum).Minimum
            if ($lowest -lt $PID) {
                Write-Log ('已有监控实例 ' + $lowest + '，本实例退出')
                exit
            }
        }
    }
} catch {
    Write-Log ('检查重复实例失败: ' + $_.Exception.Message)
}

Remove-Item -LiteralPath $stopPath -Force -ErrorAction SilentlyContinue

$state = @{}
$lastGoodConfig = $null
$configErrorLogged = ''
$currentSampleSeconds = 10
$lastSampleAt = (Get-Date).AddSeconds(-10)
$lastQuickerCheck = Get-Date
$quickerMisses = 0

while ($true) {
    if (Test-Path -LiteralPath $stopPath) {
        $script:LastEvent = '收到停止请求'
        Write-Log '收到停止请求，退出监控'
        break
    }

    $now = Get-Date
    if (($now - $lastQuickerCheck).TotalSeconds -ge 5) {
        $lastQuickerCheck = $now
        $quicker = @(Get-Process -Name Quicker -ErrorAction SilentlyContinue)
        if ($quicker.Count -gt 0) {
            $quickerMisses = 0
        } else {
            $quickerMisses++
            if ($quickerMisses -ge 2) {
                $script:LastEvent = 'Quicker 已退出'
                Write-Log 'Quicker 已退出，结束监控'
                break
            }
        }
    }

    if (($now - $lastSampleAt).TotalSeconds -lt $currentSampleSeconds) {
        Start-Sleep -Milliseconds 300
        continue
    }

    $config = $null
    try {
        $config = Read-WatchConfig
        $lastGoodConfig = $config
        $configErrorLogged = ''
    } catch {
        $msg = $_.Exception.Message
        if ($msg -ne $configErrorLogged) {
            Write-Log ('读取配置失败，沿用上次配置: ' + $msg)
            $configErrorLogged = $msg
        }
        $config = $lastGoodConfig
    }

    if ($null -eq $config) {
        Start-Sleep -Seconds 2
        continue
    }

    $gap = ($now - $lastSampleAt).TotalSeconds
    $lastSampleAt = $now
    $currentSampleSeconds = $config.sampleSeconds
    if ($gap -gt ([math]::Max(30, $config.sampleSeconds * 3))) {
        Write-Log ('检测到系统休眠/卡顿 ' + [int]$gap + ' 秒，重置空闲计时')
        foreach ($key in @($state.Keys)) {
            $state[$key].lastActive = $null
        }
        Start-Sleep -Seconds 1
        continue
    }

    $all = @()
    try {
        $all = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)
    } catch {
        Write-Log ('读取进程失败: ' + $_.Exception.Message)
        $script:LastEvent = '读取进程失败'
        Start-Sleep -Seconds 2
        continue
    }
    $nameMap = Get-NameMap $all

    $appStatuses = @()
    foreach ($app in @($config.apps)) {
        if ($null -eq $app) { continue }
        if (-not $state.ContainsKey($app.name)) {
            $state[$app.name] = [pscustomobject]@{
                lastActive = $null
                prevBytes  = [long]0
                primarySig = ''
                wasRunning = $false
                lastKill   = [datetime]::MinValue
                kills      = 0
            }
        }
        $st = $state[$app.name]
        $appProcs = @(Get-AppProcesses $app $nameMap)
        $running = ($appProcs.Count -gt 0)
        $bytes = [long]0
        if ($running) { $bytes = Get-TransferBytes $app $nameMap }

        $idleSeconds = 0
        if (-not $running) {
            $st.lastActive = $null
            $st.prevBytes = [long]0
            $st.primarySig = ''
            $st.wasRunning = $false
        } else {
            $primaryName = ConvertTo-ProcName ([string]@($app.processes)[0])
            $primaryIds = @($appProcs | Where-Object { (ConvertTo-ProcName ([string]$_.Name)) -eq $primaryName } | ForEach-Object { [string]$_.ProcessId } | Sort-Object)
            $primarySig = ($primaryIds -join ',')
            $wasRunning = [bool]$st.wasRunning
            $st.wasRunning = $true
            if ($null -eq $st.lastActive -or -not $wasRunning -or $primarySig -ne $st.primarySig) {
                $st.lastActive = $now
                $st.prevBytes = $bytes
                $st.primarySig = $primarySig
            } else {
                $delta = $bytes - $st.prevBytes
                if ($bytes -lt $st.prevBytes) {
                    $st.lastActive = $now
                } elseif ($delta -ge $app.thresholdBytes) {
                    $st.lastActive = $now
                }
                $st.prevBytes = $bytes
                $idleSeconds = [int]($now - $st.lastActive).TotalSeconds
            }

            if ($idleSeconds -ge $app.idleSeconds) {
                $script:LastEvent = ($app.name + ' 空闲清理')
                Write-Log ($app.name + ' 已连续 ' + $idleSeconds + ' 秒无传输（阈值 ' + $app.idleSeconds + ' 秒），开始结束后台')
                $result = Kill-AppProcesses $app $PID $windowsPrefix $all
                if ([int]$result.Killed -gt 0) {
                    $st.kills = [int]$st.kills + 1
                }
                $st.lastKill = $now
                $st.lastActive = $now
                $st.prevBytes = [long]0
                $st.primarySig = ''
                $st.wasRunning = $false
                if ($result.Skipped.Count -gt 0) {
                    Write-Log ('受保护/无法读取而跳过: ' + ($result.Skipped -join ', '))
                }
                Write-Log ($app.name + ' 清理完成：结束 ' + $result.Killed + ' 个进程，剩余 ' + $result.Remaining)
                if ($config.notifications -ne $false) {
                    $text = $app.name + ' 已连续 ' + [math]::Round($app.idleMinutes, 1) + ' 分钟无上传/下载，结束后台 ' + $result.Killed + ' 个进程。'
                    if ($result.Remaining -gt 0) {
                        $text = $text + ' 另有 ' + $result.Remaining + ' 个进程未能结束。'
                    }
                    Show-Balloon '传输空闲清理' $text
                }
                try {
                    $all = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)
                    $nameMap = Get-NameMap $all
                } catch {
                    $all = @()
                    $nameMap = @{}
                }
                $appProcs = @(Get-AppProcesses $app $nameMap)
                $running = ($appProcs.Count -gt 0)
                $bytes = [long]0
                $idleSeconds = 0
            }
        }

        $lastActiveText = $null
        if ($null -ne $st.lastActive) { $lastActiveText = ([datetime]$st.lastActive).ToString('o') }
        $lastKillText = $null
        if ($st.lastKill -ne [datetime]::MinValue) { $lastKillText = ([datetime]$st.lastKill).ToString('o') }

        $appStatuses += [pscustomobject]@{
            name              = [string]$app.name
            running           = $running
            processCount      = $appProcs.Count
            transferProcesses = (@($app.transferProcesses) -join ';')
            transferBytes     = $bytes
            idleSeconds       = $idleSeconds
            idleLimitSeconds  = $app.idleSeconds
            lastActive        = $lastActiveText
            kills             = $st.kills
            lastKill          = $lastKillText
        }
    }

    if ($appStatuses.Count -eq 0) { $script:LastEvent = '没有启用的软件' }
    Write-Status $appStatuses $config.sampleSeconds
    Start-Sleep -Milliseconds 500
}

try {
    Remove-Item -LiteralPath $statusPath -Force -ErrorAction SilentlyContinue
} catch {
}
Write-Log '监控退出'
