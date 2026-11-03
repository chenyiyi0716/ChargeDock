using System.Windows.Media;
using ChargeDock.Core;
using Microsoft.Win32;

namespace ChargeDock.App;

public enum ThemeMode { Light, Dark, System }

/// <summary>
/// 玻璃拟态配色 + 主题模式(浅色 / 深色 / 跟随系统)。
/// 颜色取自 docs/ui-design-v2.html(定稿版)。
/// </summary>
public static class Theme
{
    public static ThemeMode Mode = ThemeMode.System;

    /// <summary>当前是否深色(跟随系统时读注册表 AppsUseLightTheme)。</summary>
    public static bool IsDark
    {
        get
        {
            if (Mode == ThemeMode.Light) return false;
            if (Mode == ThemeMode.Dark) return true;
            return SystemUsesDarkTheme();
        }
    }

    static bool SystemUsesDarkTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (k == null) return false;
            return (int)(k.GetValue("AppsUseLightTheme") ?? 1) == 0;
        }
        catch { return false; }
    }

    /// <summary>系统是否为深色(用于托盘图标轨道色匹配任务栏)。</summary>
    public static bool SystemIsDark => SystemUsesDarkTheme();

    // ---- 玻璃卡片 ----
    /// <summary>卡片背景(分层窗口下的半透明玻璃底色,alpha 越高越不透明)。</summary>
    public static Color CardBg => IsDark
        ? Color.FromArgb(0xE8, 0x12, 0x14, 0x1C)
        : Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF);

    /// <summary>非分层窗口的实色背景(亚克力不可用时的兜底)。</summary>
    public static Color SolidCardBg => IsDark
        ? Color.FromRgb(0x11, 0x14, 0x1E)
        : Color.FromRgb(0xF8, 0xFA, 0xFD);

    /// <summary>亚克力着色(alpha 控制着色强度)。</summary>
    public static Color AcrylicTint => IsDark
        ? Color.FromArgb(0x9E, 0x11, 0x14, 0x1E)
        : Color.FromArgb(0x9E, 0xF8, 0xFA, 0xFD);

    /// <summary>卡片描边(柔和)。</summary>
    public static Color CardBorder => IsDark
        ? Color.FromArgb(26, 255, 255, 255)
        : Color.FromArgb(179, 255, 255, 255);

    /// <summary>弥散阴影颜色。</summary>
    public static Color Shadow => IsDark
        ? Color.FromArgb(179, 0, 0, 0)
        : Color.FromArgb(115, 15, 23, 42);

    // ---- 文本 ----
    public static Color Fg => IsDark
        ? Color.FromRgb(0xEE, 0xF1, 0xF6)
        : Color.FromRgb(0x1A, 0x1D, 0x24);

    /// <summary>次要文字(副标 / 元信息)。浅色背景上提高不透明度以保证可读。</summary>
    public static Color Dim => IsDark
        ? Color.FromArgb(165, 0xEE, 0xF1, 0xF6)
        : Color.FromArgb(165, 0x1A, 0x1D, 0x24);

    /// <summary>更弱的文字(分组标题 / 状态)。浅色背景上提高不透明度以避免过淡。</summary>
    public static Color Muted => IsDark
        ? Color.FromArgb(145, 0xEE, 0xF1, 0xF6)
        : Color.FromArgb(150, 0x1A, 0x1D, 0x24);

    // ---- 图标 ----
    public static Color IconBg => IsDark
        ? Color.FromArgb(20, 255, 255, 255)
        : Color.FromArgb(13, 0, 0, 0);

    public static Color IconFg => IsDark
        ? Color.FromRgb(0xCB, 0xD5, 0xE1)
        : Color.FromRgb(0x33, 0x41, 0x55);

    // ---- 分隔线 / 轨道 ----
    public static Color Divider => IsDark
        ? Color.FromArgb(18, 255, 255, 255)
        : Color.FromArgb(15, 0, 0, 0);

    public static Color BarTrack => IsDark
        ? Color.FromArgb(26, 255, 255, 255)
        : Color.FromArgb(20, 0, 0, 0);

    // ---- 控件 ----
    public static Color ToggleOn => Color.FromRgb(0x63, 0x66, 0xF1);

    public static Color ToggleOff => IsDark
        ? Color.FromArgb(70, 255, 255, 255)
        : Color.FromArgb(62, 0, 0, 0);

    public static Color SegBg => IsDark
        ? Color.FromArgb(26, 255, 255, 255)
        : Color.FromArgb(15, 0, 0, 0);

    public static Color SegOnBg => IsDark
        ? Color.FromArgb(235, 255, 255, 255)
        : Colors.White;

    public static Color SegOnFg => Color.FromRgb(0x11, 0x11, 0x11);

    /// <summary>标题栏右侧「已连接」状态点。</summary>
    public static Color StatusDot => Color.FromRgb(0x22, 0xC5, 0x5E);

    /// <summary>菜单项悬停底色。</summary>
    public static Color HoverBg => IsDark
        ? Color.FromArgb(20, 255, 255, 255)
        : Color.FromArgb(15, 0, 0, 0);

    // ---- 电量颜色(条 + 百分比) ----
    public static Color GreenStart => Color.FromRgb(0x34, 0xD3, 0x99);
    public static Color GreenEnd => Color.FromRgb(0x10, 0xB9, 0x81);
    public static Color AmberStart => Color.FromRgb(0xFB, 0xBF, 0x24);
    public static Color AmberEnd => Color.FromRgb(0xF5, 0x9E, 0x0B);
    public static Color RedStart => Color.FromRgb(0xFB, 0x71, 0x85);
    public static Color RedEnd => Color.FromRgb(0xEF, 0x44, 0x44);

    /// <summary>电量条渐变画刷(绿 / 黄 / 红)。</summary>
    public static LinearGradientBrush LevelBarBrush(int level)
    {
        Color a, b;
        switch (BatteryColorPolicy.Classify(level))
        {
            case BatteryLevelClass.Mid: a = AmberStart; b = AmberEnd; break;
            case BatteryLevelClass.Low: a = RedStart; b = RedEnd; break;
            default: a = GreenStart; b = GreenEnd; break;
        }
        var brush = new LinearGradientBrush(a, b, new System.Windows.Point(0, 0.5), new System.Windows.Point(1, 0.5));
        brush.Freeze();
        return brush;
    }

    /// <summary>百分比文字颜色:低电量红色,其余跟随前景色。</summary>
    public static Color LevelTextColor(int level) =>
        BatteryColorPolicy.Classify(level) == BatteryLevelClass.Low ? RedEnd : Fg;

    /// <summary>电量对应的纯色(托盘图标等需要单色处使用)。</summary>
    public static Color LevelColor(int level) => BatteryColorPolicy.Classify(level) switch
    {
        BatteryLevelClass.Mid => AmberEnd,
        BatteryLevelClass.Low => RedEnd,
        _ => GreenEnd,
    };
}
