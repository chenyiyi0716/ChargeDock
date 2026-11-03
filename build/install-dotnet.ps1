$ErrorActionPreference = 'Stop'

# ---- 决定安装目录 ----
$isAdmin = $false
try {
    $id = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $p  = New-Object System.Security.Principal.WindowsPrincipal($id)
    $isAdmin = $p.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
} catch { $isAdmin = $false }
Write-Output ("elevated = " + $isAdmin)

if ($isAdmin) {
    $InstallDir = 'C:\Program Files\dotnet'
} else {
    $InstallDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
}
Write-Output ("InstallDir = " + $InstallDir)

# ---- 下载官方 dotnet-install.ps1 ----
$tmp = Join-Path $env:TEMP 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $tmp -UseBasicParsing
Write-Output ("downloaded install script -> " + $tmp)

# ---- 安装 .NET 8 SDK (latest 8.0 patch) ----
& powershell -NoProfile -ExecutionPolicy Bypass -File $tmp -Channel 8.0 -InstallDir $InstallDir

Write-Output "=== verify ==="
$d = Join-Path $InstallDir 'dotnet.exe'
& $d --version
& $d --list-sdks
