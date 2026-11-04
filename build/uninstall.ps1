# ChargeDock uninstall (portable build)
# Usage: right-click "Run with PowerShell", or:  .\build\uninstall.ps1
# Cleans: process / startup entry / registry config / program dir / desktop shortcut
# NOTE: keep this file ASCII-only - Windows PowerShell 5.1 misparses UTF-8 without BOM.

$ErrorActionPreference = 'SilentlyContinue'
Write-Host '== ChargeDock uninstall ==' -ForegroundColor Cyan

$progDir = Join-Path $env:LOCALAPPDATA 'Programs\ChargeDock'
$runKey  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$cfgKey  = 'HKCU:\Software\ChargeDock'
$lnk     = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ChargeDock.lnk'

# 1) stop the running instance (it locks the exe)
$p = Get-Process ChargeDock -ErrorAction SilentlyContinue
if ($p) {
    $p | Stop-Process -Force
    Start-Sleep -Milliseconds 1500
    Write-Host ' - process stopped'
}

# 2) remove the startup entry
if (Get-ItemProperty -Path $runKey -Name 'ChargeDock' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKey -Name 'ChargeDock'
    Write-Host ' - startup entry removed'
}

# 3) remove settings (theme / toggles)
if (Test-Path $cfgKey) {
    Remove-Item -Path $cfgKey -Recurse -Force
    Write-Host ' - settings removed'
}

# 4) remove the program dir
if (Test-Path $progDir) {
    Remove-Item -Path $progDir -Recurse -Force
    Write-Host " - removed $progDir"
}

# 5) remove the desktop shortcut
if (Test-Path $lnk) {
    Remove-Item $lnk -Force
    Write-Host ' - shortcut removed'
}

Write-Host ''
Write-Host 'ChargeDock uninstalled.' -ForegroundColor Green
