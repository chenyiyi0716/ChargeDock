using System;
using System.IO;
using Microsoft.Win32;

namespace ChargeDock.App;

/// <summary>
/// 配置持久化:所有开关都存在 HKCU\Software\ChargeDock,不需要管理员权限。
/// </summary>
public static class Settings
{
    const string Root = @"Software\ChargeDock";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "ChargeDock";

    static string? ReadString(string name, string? fallback = null)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Root, false);
        return k?.GetValue(name) as string ?? fallback;
    }

    static int ReadInt(string name, int fallback)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Root, false);
        object? v = k?.GetValue(name);
        return v == null ? fallback : Convert.ToInt32(v);
    }

    static bool ReadBool(string name, bool fallback)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Root, false);
        object? v = k?.GetValue(name);
        return v == null ? fallback : Convert.ToInt32(v) != 0;
    }

    static void Write(string name, object value, RegistryValueKind kind)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Root);
        k?.SetValue(name, value, kind);
    }

    /// <summary>开机自启(写 HKCU Run 键)。</summary>
    public static bool StartupEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return k != null && k.GetValue(RunName) != null;
        }
        set
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (value)
            {
                string exe = System.Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, "ChargeDock.exe");
                k.SetValue(RunName, "\"" + exe + "\" --tray");
            }
            else
            {
                k.DeleteValue(RunName, false);
            }
        }
    }

    /// <summary>主题模式:light / dark / system,默认跟随系统。</summary>
    public static string ThemeMode
    {
        get => ReadString("Theme") ?? "system";
        set => Write("Theme", value, RegistryValueKind.String);
    }

    /// <summary>刷新间隔(秒):固定 10 秒,不再可配置。</summary>
    public static int RefreshIntervalSeconds
    {
        get => 10;
        set { }
    }

    /// <summary>低电量提醒总开关(鼠标 / 键盘):固定开启。</summary>
    public static bool LowBatteryEnabled
    {
        get => true;
        set { }
    }

    /// <summary>低电量提醒阈值(百分比):固定 15。</summary>
    public static int LowBatteryThreshold
    {
        get => 15;
        set { }
    }

    /// <summary>低电量时弹窗提醒,默认开。</summary>
    public static bool PopupNotify
    {
        get => ReadBool("PopupNotify", true);
        set => Write("PopupNotify", value ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>低电量时卡片变色,默认开。</summary>
    public static bool CardColorChange
    {
        get => ReadBool("CardColorChange", true);
        set => Write("CardColorChange", value ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>是否已提示过"图标在托盘里"(首次启动弹一次气泡)。</summary>
    public static bool TipShown
    {
        get => ReadBool("TipShown", false);
        set => Write("TipShown", value ? 1 : 0, RegistryValueKind.DWord);
    }
}
