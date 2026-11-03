# ChargeDock 打包脚本:发布主程序(自包含单文件) → 内嵌进安装器 → 产出单文件安装包
param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$repo        = Split-Path -Parent $PSScriptRoot
$app         = Join-Path $repo 'src\ChargeDock.App\ChargeDock.App.csproj'
$installer   = Join-Path $repo 'src\ChargeDock.Installer\ChargeDock.Installer.csproj'
$payloadDir  = Join-Path $repo 'src\ChargeDock.Installer\payload'
$publishDir  = Join-Path $repo 'artifacts\publish'
$outDir      = Join-Path $repo 'artifacts'

if (-not $SkipTests) {
    & (Join-Path $PSScriptRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw "build/test failed" }
}

# 1) 发布主程序:自包含单文件
& $dotnet publish $app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir -nologo
if ($LASTEXITCODE -ne 0) { throw "app publish failed" }

# 1.1) 给主程序签名(先签后嵌入,安装后解出来的主程序也是已签名的)
& (Join-Path $PSScriptRoot 'sign.ps1') -Files @((Join-Path $publishDir 'ChargeDock.exe'))

# 2) 复制进 installer payload
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null
Copy-Item (Join-Path $publishDir 'ChargeDock.exe') (Join-Path $payloadDir 'ChargeDock.exe') -Force

# 3) 强制清理安装器 obj/bin
#    EmbeddedResource 的增量构建会缓存旧 payload,不清理就会把旧主程序嵌进安装包(重装仍是旧 UI)。
Remove-Item (Join-Path $repo 'src\ChargeDock.Installer\obj') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $repo 'src\ChargeDock.Installer\bin') -Recurse -Force -ErrorAction SilentlyContinue

# 3.1) 发布安装器:自包含单文件(文档要求的"单文件安装 EXE")
& $dotnet publish $installer -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $outDir -nologo
if ($LASTEXITCODE -ne 0) { throw "installer publish failed" }

# 3.2) 给安装包签名
& (Join-Path $PSScriptRoot 'sign.ps1') -Files @((Join-Path $outDir 'ChargeDock-Setup.exe'))

Write-Host ""
Write-Host "产出:" -ForegroundColor Green
Get-ChildItem $outDir -Filter 'ChargeDock-Setup.exe' | ForEach-Object {
    Write-Host ("  " + $_.FullName + "  (" + [math]::Round($_.Length / 1MB, 1) + " MB)")
}
