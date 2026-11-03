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
if (-not (Test-Path $dotnet)) {
    # 兜底:尝试系统 PATH 上的 dotnet
    $dotnet = 'dotnet'
}

$repo = Split-Path -Parent $PSScriptRoot
$sln  = Join-Path $repo 'ChargeDock.sln'

Write-Host "dotnet = $dotnet" -ForegroundColor DarkGray

& $dotnet restore $sln -nologo
if ($LASTEXITCODE -ne 0) { throw "restore failed" }

if (-not $NoTest) {
    & $dotnet build $sln -c Release -nologo
    if ($LASTEXITCODE -ne 0) { throw "build failed" }

    & $dotnet test (Join-Path $repo 'tests\ChargeDock.Tests\ChargeDock.Tests.csproj') -c Release --no-build -nologo
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}
else {
    & $dotnet build $sln -c Release -nologo
    if ($LASTEXITCODE -ne 0) { throw "build failed" }
}

if ($Publish) {
    & $dotnet publish (Join-Path $repo 'src\ChargeDock.App\ChargeDock.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $repo 'artifacts\publish')
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
}

Write-Host "OK" -ForegroundColor Green
