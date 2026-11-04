# ChargeDock 卸载脚本(便携版)
# 用法:右键「使用 PowerShell 运行」,或在终端执行  .\build\uninstall.ps1
# 清理内容:进程 / 开机自启 / 注册表配置 / 程序目录 / 桌面快捷方式

$ErrorActionPreference = 'SilentlyContinue'
Write-Host '== ChargeDock 卸载 ==' -ForegroundColor Cyan

$progDir = Join-Path $env:LOCALAPPDATA 'Programs\ChargeDock'
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$cfgKey  = 'HKCU:\Software\ChargeDock'
$lnk     = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ChargeDock.lnk'

# 1) 结束进程(程序运行时会锁文件,必须先退出)
$p = Get-Process ChargeDock -ErrorAction SilentlyContinue
if ($p) {
    $p | Stop-Process -Force
    Start-Sleep -Milliseconds 1500
    Write-Host '· 已结束进程'
}

# 2) 移除开机自启
if (Get-ItemProperty -Path $runKey -Name 'ChargeDock' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKey -Name 'ChargeDock'
    Write-Host '· 已移除开机自启'
}

# 3) 删除配置(主题 / 开关等全部设置)
if (Test-Path $cfgKey) {
    Remove-Item -Path $cfgKey -Recurse -Force
    Write-Host '· 已删除注册表配置 HKCU\Software\ChargeDock'
}

# 4) 删除程序目录
if (Test-Path $progDir) {
    Remove-Item -Path $progDir -Recurse -Force
    Write-Host "· 已删除程序目录 $progDir"
}

# 5) 删除桌面快捷方式
if (Test-Path $lnk) {
    Remove-Item $lnk -Force
    Write-Host '· 已删除桌面快捷方式'
}

Write-Host ''
Write-Host '✓ ChargeDock 已卸载完毕' -ForegroundColor Green
