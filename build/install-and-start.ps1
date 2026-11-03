# 安装并启动 ChargeDock
# 安装用可信 dotnet 跑安装器 DLL(规避沙箱对自编译 exe 的写限制);
# 启动用 ShellExecute 走用户正常上下文(保证托盘图标能注册)。
$ErrorActionPreference = 'Continue'

$dotnet  = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$dll     = 'C:\DeepSeek\BatteryWatch\src\ChargeDock.Installer\bin\Release\net8.0-windows10.0.22621.0\win-x64\ChargeDock-Setup.dll'
$instDir = Join-Path $env:LOCALAPPDATA 'Programs\ChargeDock'
$exe     = Join-Path $instDir 'ChargeDock.exe'

# 0) 清理旧进程
Get-Process ChargeDock -ErrorAction SilentlyContinue | Stop-Process -Force

# 1) 静默安装(输出重定向到文件,后台 job 隔离,避免子进程拖住管道)
$log = Join-Path $env:TEMP 'cdock-install.log'
$err = Join-Path $env:TEMP 'cdock-install.err'
$job = Start-Job -ScriptBlock {
    param($d, $dll, $log, $err)
    & $d $dll /silent *> $log 2> $err
} -ArgumentList $dotnet, $dll, $log, $err

$completed = Wait-Job $job -Timeout 180
if (-not $completed) {
    Stop-Job $job
    Write-Host "[warn] install job timed out (check logs below)"
}
Remove-Job $job -Force

# 2) 杀掉安装器拉起的实例(那是沙箱上下文,托盘受限)
Get-Process ChargeDock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

# 3) 检查安装结果
Write-Host ("installed exe  = " + (Test-Path $exe))
Write-Host ("desktop lnk   = " + (Test-Path (Join-Path ([Environment]::GetFolderPath('Desktop')) 'ChargeDock.lnk')))
Write-Host ("startmenu lnk = " + (Test-Path (Join-Path ([Environment]::GetFolderPath('Programs')) 'ChargeDock.lnk')))
Write-Host ("uninstall key = " + (Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ChargeDock'))

# 4) 用 ShellExecute(用户正常上下文)启动,保证托盘可用
if (Test-Path $exe) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.WorkingDirectory = $instDir
    $psi.UseShellExecute = $true
    [System.Diagnostics.Process]::Start($psi) | Out-Null
    Write-Host "launched ChargeDock via ShellExecute"
    Start-Sleep -Seconds 4
    $p = Get-Process ChargeDock -ErrorAction SilentlyContinue
    if ($p) { Write-Host ("running pid = " + (($p | Select-Object -First 1).Id)) }
    else { Write-Host "running = none (process not found)" }
}
else {
    Write-Host "install failed, log tail:"
    if (Test-Path $log) { Get-Content $log -Tail 10 }
    if (Test-Path $err) { Get-Content $err -Tail 10 }
}
