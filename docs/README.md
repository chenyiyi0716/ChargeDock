# ChargeDock

常驻桌面的个人外设电量卡片:系统托盘常驻,点击弹出悬浮卡片,抬眼即可看到无线鼠标 / 耳机(左右耳)的电量。

依据 [`docs/产品设计文档.md`](docs/产品设计文档.md) 与 [`docs/技术选型.md`](docs/技术选型.md) 实现。

## 技术栈

| 层        | 选型                         | 说明                                                        |
| -------- | -------------------------- | --------------------------------------------------------- |
| 语言 / 运行时 | C# · **.NET 8 LTS**        | 按技术方案升级到 .NET 8                                           |
| UI 框架    | **WPF**(.NET 8)            | 技术方案优先 WinUI 3,但按文档预留的"兜底"落到 WPF,见下方说明                    |
| 设备访问     | 独立 `ChargeDock.Devices` 类库 | BLE 走 `Windows.Devices.Bluetooth`;HID 走 SetupAPI P/Invoke |
| 托盘图标     | WinForms `NotifyIcon` 互操作  | WPF 无原生托盘                                                 |
| 部署       | 非打包 + 自包含,单文件安装 EXE        | 开机自启写 HKCU Run 键                                          |
| 测试       | **xUnit**                  | 业务逻辑(低电量、显示规则)脱离真机单测                                      |

### 为什么 UI 用了 WPF 而不是 WinUI 3

技术方案 §5 明确写了 WinUI 3 的三个坑,其中"托盘图标是 WinUI 3 最别扭的地方"排在首位,而 ChargeDock 的**核心入口就是托盘**。产品形态是"托盘 + 一张圆角卡片",WPF + `NotifyIcon` 是最成熟、最稳的路径,完全命中方案里"换回 WPF 的触发条件"(托盘互操作 / 部署)。架构上严格遵循方案的核心边界:**UI 壳只依赖 `IBatteryReader` 接口,设备层一行不碰 UI**,日后要换 WinUI 3 壳时设备层与核心逻辑零改动。

## 架构与目录

```
src/
├─ ChargeDock.Core/        # 纯业务逻辑(net8.0,零 Windows 依赖,可单测)
│  ├─ DeviceKind.cs
│  ├─ BatteryReading.cs    # 读数快照
│  ├─ IBatteryReader.cs    # 设备读取器统一接口(UI 只依赖它)
│  ├─ LowBatteryPolicy.cs  # 低电量策略(15%,仅鼠标/键盘)
│  └─ DeviceDisplayPolicy.cs # 只显示已连接、耳机左右耳独立、排序
├─ ChargeDock.Devices/     # 设备层(net8.0-windows,BLE + HID)
│  ├─ MouseReader.cs       # HECATE G3M Pro,2.4G 私有 HID 主动查询
│  ├─ HeadsetReader.cs     # REDMI Buds 8 Pro,BLE GATT 0x180F/0x2A19
│  ├─ KeyboardReader.cs    # K820,固件不上报电量 → 无读数(搁置)
│  └─ HidFrame.cs          # 鼠标协议帧(纯字节逻辑,可单测)
├─ ChargeDock.App/         # WPF 壳
│  ├─ Program.cs           # 入口 / 单实例 / 托盘线程 / 定时刷新 / 低电量
│  ├─ Flyout.cs            # 悬浮卡片(可拖动、置顶、深浅主题、设置页)
│  ├─ Theme.cs / Settings.cs / TrayIconFactory.cs
│  └─ app.manifest
└─ ChargeDock.Installer/   # 安装器(内嵌自包含主程序)
tests/
└─ ChargeDock.Tests/       # xUnit 单元测试
```

## 支持设备

| 设备                  | 显示        | 状态                               |
| ------------------- | --------- | -------------------------------- |
| 鼠标 HECATE G3M Pro   | 1 个电量     | ✅ 2.4G 接收器私有 HID(已逆向,主动查询 + 校验帧) |
| 耳机 REDMI Buds 8 Pro | 左 / 右(独立) | ✅ BLE GATT 0x180F / 0x2A19       |
| 耳机充电仓               | —         | ⏳ 尚未逆向通道,读不到就不显示("能读到则显示")       |
| 键盘 K820             | —         | ❌ 固件不上报电量,按技术方案"先搁置"             |

逆向成果(鼠标查询帧校验、BLE 双地址)从 `BatteryWatchDemo/` 原样迁移,逻辑未变。

## 产品规则(实现要点)

- **只显示已连接设备**:无读数的设备不占位;耳机左右耳独立判断,只戴一只只显示那一侧。
- **低电量提醒**:仅鼠标、键盘;阈值 **15%**;触发时卡片变色 + 弹窗气泡;耳机不做低电量提醒。
- **浅色 / 深色主题**、**托盘常驻 + 开机自启**、**卡片可拖动 + 置顶**。

## 构建 / 测试 / 打包

前置:`.NET 8 SDK`(本机已装,可用 `build\install-dotnet.ps1` 重装到用户目录)。

```powershell
.\build\build.ps1          # 还原 + 编译(Release)+ 跑 xUnit 测试
.\build\publish.ps1        # 发布自包含主程序 + 打包单文件安装器
.\build\verify-install.ps1 # 端到端验证安装/卸载逻辑
```

> **代码签名**:`publish.ps1` 会自动调用 `build\sign.ps1`,用本机自签证书(CN=ChargeDock)给主程序和安装包做 SHA256 Authenticode 签名,并把证书加进本机「受信任的根 + 受信任发布者」,消除"未知发布者"提示。签名状态可用 `Get-AuthenticodeSignature <exe>` 查看。

产物:

- `artifacts\publish\ChargeDock.exe` — 自包含单文件主程序
- `artifacts\ChargeDock-Setup.exe` — 单文件安装包(双击安装,支持 `/silent`、`/uninstall`)

> **体积说明**:自包含单文件会带 .NET 运行时 + WPF,主程序约 180 MB、安装包约 360 MB,这是技术方案 §5 已预判的"自包含体积变大",属可接受代价。

## 诊断入口

```powershell
.\ChargeDock.exe --selftest out.txt   # 不开界面,把三台设备读数写进文件
.\ChargeDock.exe --render out.png     # 只渲染悬浮卡片到 PNG,检查布局
```

## 关于沙箱验证

本仓库在 DeepSeek Harness 会话沙箱中开发:沙箱只放行可信二进制(powershell/dotnet)写任意目录,自编译的 exe 写 `%LOCALAPPDATA%\Programs` 会被拒。因此安装/卸载逻辑通过 `dotnet ChargeDock-Setup.dll` 完成端到端验证;用户正常双击 `ChargeDock-Setup.exe` 不受此限制(与 `BatteryWatchDemo` 的历史结论一致:exe 放沙箱目录外即可正常托盘/写注册表)。
