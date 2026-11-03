# ChargeDock 部署(参照 BatteryWatch demo 的方法):
#   1) 构建【未签名】自包含单文件 —— 自签证书会触发 SmartScreen「Windows 已保护你的电脑」
#   2) 放到 %LOCALAPPDATA%\Programs\ChargeDock\ —— 不在 harness 目录树下,避免被沙箱化(托盘注册不上)
#   3) 建桌面快捷方式,双击即运行
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$repo    = Split-Path -Parent $PSScriptRoot
$app     = Join-Path $repo 'src\ChargeDock.App\ChargeDock.App.csproj'
$publish = Join-Path $repo 'artifacts\publish'
$install = Join-Path $env:LOCALAPPDATA 'Programs\ChargeDock'
$exe     = Join-Path $install 'ChargeDock.exe'

if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "build/test failed" }
}

# 1) 发布自包含单文件(未签名)
& $dotnet publish $app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish -nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# 2) 停旧进程 → 复制到 %LOCALAPPDATA%\Programs\ChargeDock\
Get-Process ChargeDock -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 900
New-Item -ItemType Directory -Path $install -Force | Out-Null
Copy-Item (Join-Path $publish 'ChargeDock.exe') $exe -Force
try { Unblock-File -Path $exe -ErrorAction SilentlyContinue } catch { }

# 3) 桌面快捷方式
$deskLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ChargeDock.lnk'
$shell = New-Object -ComObject WScript.Shell
$sc = $shell.CreateShortcut($deskLnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $install
$sc.Description = 'ChargeDock'
$sc.IconLocation = "$exe,0"
$sc.Save()

# 4) 启动
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exe
$psi.WorkingDirectory = $install
$psi.UseShellExecute = $true
[System.Diagnostics.Process]::Start($psi) | Out-Null

Write-Host ""
Write-Host "部署完成:" -ForegroundColor Green
Write-Host ("  exe      : " + $exe)
Write-Host ("  shortcut : " + $deskLnk)
