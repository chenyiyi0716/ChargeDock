# 需要管理员权限:关闭 SmartScreen 的「检查应用和文件」,避免自编译/自签 exe 被拦
# 可通过「设置 → 隐私和安全性 → Windows 安全中心 → 应用和浏览器控制 → 基于声誉的保护」重新开启
$ErrorActionPreference = 'Stop'

$policy = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
New-Item -Path $policy -Force | Out-Null
Set-ItemProperty -Path $policy -Name 'EnableSmartScreen' -Value 0 -Type DWord

$explorer = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer'
Set-ItemProperty -Path $explorer -Name 'SmartScreenEnabled' -Value 'Off' -Type String

# 提示:部分策略在重启资源管理器 / 注销后生效,这里尝试刷新
try { Stop-Process -Name 'smartscreen' -Force -ErrorAction SilentlyContinue } catch { }

Write-Host 'SmartScreen 已关闭(EnableSmartScreen=0)。可能需要注销或重启后完全生效。' -ForegroundColor Green
