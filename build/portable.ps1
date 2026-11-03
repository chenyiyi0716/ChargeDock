# ChargeDock 便携版构建:自包含单文件 exe,点击即运行(不安装)
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$repo    = Split-Path -Parent $PSScriptRoot
$app     = Join-Path $repo 'src\ChargeDock.App\ChargeDock.App.csproj'
$dist    = Join-Path $repo 'dist'
$publish = Join-Path $repo 'artifacts\publish'

if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "build/test failed" }
}

# 发布自包含单文件 exe(便携:双击即运行,无需安装)
& $dotnet publish $app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish -nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# 签名
& (Join-Path $PSScriptRoot 'sign.ps1') -Files @((Join-Path $publish 'ChargeDock.exe'))

# 复制到 dist/
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$exe = Join-Path $dist 'ChargeDock.exe'
Copy-Item (Join-Path $publish 'ChargeDock.exe') $exe -Force

# 桌面快捷方式(指向便携 exe)
$deskLnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ChargeDock.lnk'
$shell = New-Object -ComObject WScript.Shell
$sc = $shell.CreateShortcut($deskLnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $dist
$sc.Description = 'ChargeDock'
$sc.IconLocation = "$exe,0"
$sc.Save()

Write-Host ""
Write-Host "便携版产出:" -ForegroundColor Green
Write-Host ("  exe      : " + $exe)
Write-Host ("  shortcut : " + $deskLnk)
