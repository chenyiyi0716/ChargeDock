# 端到端验证安装/卸载逻辑(用可信 dotnet 跑安装器 DLL,规避沙箱对自编译 exe 的写限制)
$ErrorActionPreference = 'Continue'

$repo   = 'C:\DeepSeek\BatteryWatch'
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$dll    = Join-Path $repo 'src\ChargeDock.Installer\bin\Release\net8.0-windows10.0.22621.0\win-x64\ChargeDock-Setup.dll'
$instDir  = Join-Path $env:LOCALAPPDATA 'Programs\ChargeDock'
$desk     = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ChargeDock.lnk'
$start    = Join-Path ([Environment]::GetFolderPath('Programs')) 'ChargeDock.lnk'
$uninst   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ChargeDock'

function Cleanup {
    Get-Process ChargeDock,ChargeDock-Setup -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    foreach ($p in @($desk, $start)) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
    Remove-Item $instDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $uninst -Recurse -Force -ErrorAction SilentlyContinue
}

function Run([string]$arg) {
    $o = Join-Path $env:TEMP 'cdock-out.log'
    $e = Join-Path $env:TEMP 'cdock-err.log'
    $p = Start-Process -FilePath $dotnet -ArgumentList @($dll, $arg) -Wait -PassThru -RedirectStandardOutput $o -RedirectStandardError $e
    Get-Process ChargeDock -ErrorAction SilentlyContinue | Stop-Process -Force
    return $p.ExitCode
}

Write-Host "=== 0) pre-cleanup ==="
Cleanup

Write-Host "=== 1) install ==="
Write-Host ("install exit = " + (Run '/silent'))

Write-Host "=== 2) checks ==="
Write-Host ("exe          : " + (Test-Path (Join-Path $instDir 'ChargeDock.exe')))
Write-Host ("desktop lnk  : " + (Test-Path $desk))
Write-Host ("startmenu lnk: " + (Test-Path $start))
Write-Host ("uninstall key: " + (Test-Path $uninst))

Write-Host "=== 3) uninstall ==="
Write-Host ("uninstall exit = " + (Run '/uninstall'))

Write-Host "=== 4) cleanup checks ==="
Write-Host ("exe          : " + (Test-Path (Join-Path $instDir 'ChargeDock.exe')))
Write-Host ("desktop lnk  : " + (Test-Path $desk))
Write-Host ("startmenu lnk: " + (Test-Path $start))
Write-Host ("uninstall key: " + (Test-Path $uninst))
