# 初始化/维护解决方案文件:创建 ChargeDock.sln 并把已存在的项目加入
$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$repo = Split-Path -Parent $PSScriptRoot
$sln  = Join-Path $repo 'ChargeDock.sln'

if (-not (Test-Path $sln)) {
    & $dotnet new sln -n ChargeDock -o $repo
    if ($LASTEXITCODE -ne 0) { throw "sln create failed" }
}

$projects = @(
    'src\ChargeDock.Core\ChargeDock.Core.csproj',
    'src\ChargeDock.Devices\ChargeDock.Devices.csproj',
    'tests\ChargeDock.Tests\ChargeDock.Tests.csproj',
    'src\ChargeDock.App\ChargeDock.App.csproj',
    'src\ChargeDock.Installer\ChargeDock.Installer.csproj'
)

foreach ($p in $projects) {
    $full = Join-Path $repo $p
    if (Test-Path $full) {
        & $dotnet sln $sln add $full
        if ($LASTEXITCODE -ne 0) { Write-Warning "add failed: $p" }
    }
}

Write-Host "sln ready: $sln"
