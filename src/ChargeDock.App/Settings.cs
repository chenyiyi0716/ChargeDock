using System;
using System.Reflection;
using Microsoft.Win32;

namespace ChargeDock.App;

/// <summary>
/// 配置持久化:所有开关都存在 HKCU,不需要管理员权限。
///   Notify / DarkTheme / TipShown → HKCU\Software\ChargeDock
///   开机自启 → HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ChargeDock
/// </summary>
public static class Settings
{
    const string Root = @"Software\ChargeDock";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "ChargeDock";

    static bool? ReadBool(string name, bool? fallback = null)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Root, false);
        if (k == null) return fallback;
        object? v = k.GetValue(name);
        return v == null ? fallback : Convert.ToInt32(v) != 0;
    }

    static void WriteBool(string name, bool value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Root);
        k?.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }

    /// <summary>低电量提醒开关,默认开。</summary>
    public static bool NotifyEnabled
    {
        get => ReadBool("Notify") ?? true;
        set => WriteBool("Notify", value);
    }

    /// <summary>是否使用深色主题,默认 true。</summary>
    public static bool DarkTheme
    {
        get => ReadBool("DarkTheme") ?? true;
        set => WriteBool("DarkTheme", value);
    }

    /// <summary>是否已提示过"图标在托盘里"(首次启动弹一次气泡)。</summary>
    public static bool TipShown
    {
        get => ReadBool("TipShown") ?? false;
        set => WriteBool("TipShown", value);
    }

    /// <summary>开机自启。判断依据是 Run 键里有没有我们的值。</summary>
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
                string exe = Assembly.GetExecutingAssembly().Location;
                k.SetValue(RunName, "\"" + exe + "\" --tray");
            }
            else
            {
                k.DeleteValue(RunName, false);
            }
        }
    }
}
