# 用 xunit.runner.console 跑测试(规避 VSTest testhost 在沙箱中 OpenProcess 被拒的问题)
param(
    [string]$TestDll
)
$ErrorActionPreference = 'Stop'

$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$repo = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $repo '.tools\xunit-console'
$ver = '2.9.2'
$nupkg = Join-Path $cache ("xunit.runner.console." + $ver + ".nupkg")

if (-not (Test-Path $nupkg)) {
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $url = "https://api.nuget.org/v3-flatcontainer/xunit.runner.console/$ver/xunit.runner.console.$ver.nupkg"
    Write-Host "download $url"
    Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
}

$extract = Join-Path $cache $ver
if (-not (Test-Path $extract)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $extract)
}

$console = Join-Path $extract 'tools\net8.0\xunit.console.dll'
if (-not (Test-Path $console)) {
    $console = Join-Path $extract 'tools\net6.0\xunit.console.dll'
}
if (-not (Test-Path $console)) {
    throw "xunit.console.dll not found in $extract"
}

if (-not $TestDll) {
    $base = Join-Path $repo 'tests\ChargeDock.Tests\bin'
    $rel = Join-Path $base 'Release\net8.0-windows10.0.22621.0\ChargeDock.Tests.dll'
    $dbg = Join-Path $base 'Debug\net8.0-windows10.0.22621.0\ChargeDock.Tests.dll'
    $TestDll = if (Test-Path $rel) { $rel } else { $dbg }
}

Write-Host "runner: $console"
Write-Host "dll   : $TestDll"
$env:DOTNET_ROLL_FORWARD = 'LatestMajor'
& $dotnet $console $TestDll -nologo -noappdomain
exit $LASTEXITCODE
