# ChargeDock 构建脚本
# 用法:
#   .\build.ps1            # 还原 + 编译 + 跑单元测试
#   .\build.ps1 -NoTest    # 只编译
#   .\build.ps1 -Publish   # 编译 + 发布自包含单文件(用于打包安装)
param(
    [switch]$NoTest,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$repo = Split-Path -Parent $PSScriptRoot
$sln  = Join-Path $repo 'ChargeDock.sln'

Write-Host "dotnet = $dotnet" -ForegroundColor DarkGray

& $dotnet restore $sln -nologo
if ($LASTEXITCODE -ne 0) { throw "restore failed" }

& $dotnet build $sln -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }

if (-not $NoTest) {
    # 用 xunit 控制台运行器跑测试(规避 VSTest testhost 在沙箱中 OpenProcess 被拒的问题)
    & (Join-Path $PSScriptRoot 'run-tests.ps1')
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

if ($Publish) {
    & $dotnet publish (Join-Path $repo 'src\ChargeDock.App\ChargeDock.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $repo 'artifacts\publish')
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
}

Write-Host "OK" -ForegroundColor Green
