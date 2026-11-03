using System.Windows.Media;
using ChargeDock.Core;
using Color = System.Windows.Media.Color;
namespace ChargeDock.App;

public enum ThemeMode { Dark, Light }

/// <summary>配色。切主题时改 <see cref="Mode"/> 然后重建界面即可。</summary>
public static class Theme
{
    public static ThemeMode Mode = ThemeMode.Dark;

    public static Color Bg => Mode == ThemeMode.Dark ? Color.FromRgb(0x17, 0x17, 0x1C) : Color.FromRgb(0xFF, 0xFF, 0xFF);
    public static Color Card => Mode == ThemeMode.Dark ? Color.FromRgb(0x20, 0x20, 0x27) : Color.FromRgb(0xF4, 0xF4, 0xF7);
    public static Color Border => Mode == ThemeMode.Dark ? Color.FromRgb(0x2E, 0x2E, 0x38) : Color.FromRgb(0xE4, 0xE4, 0xEA);
    public static Color Divider => Mode == ThemeMode.Dark ? Color.FromRgb(0x2A, 0x2A, 0x33) : Color.FromRgb(0xEC, 0xEC, 0xF0);
    public static Color Track => Mode == ThemeMode.Dark ? Color.FromRgb(0x33, 0x33, 0x3D) : Color.FromRgb(0xE2, 0xE2, 0xE8);
    public static Color Fg => Mode == ThemeMode.Dark ? Color.FromRgb(0xEC, 0xEC, 0xF1) : Color.FromRgb(0x1A, 0x1A, 0x1C);
    public static Color Muted => Mode == ThemeMode.Dark ? Color.FromRgb(0x8A, 0x8A, 0x99) : Color.FromRgb(0x6B, 0x6B, 0x75);
    public static Color Dim => Mode == ThemeMode.Dark ? Color.FromRgb(0x5A, 0x5A, 0x66) : Color.FromRgb(0x6B, 0x72, 0x80);
    public static Color Green => Mode == ThemeMode.Dark ? Color.FromRgb(0x3D, 0xDC, 0x97) : Color.FromRgb(0x12, 0xA1, 0x5F);
    public static Color Amber => Mode == ThemeMode.Dark ? Color.FromRgb(0xFF, 0xCB, 0x47) : Color.FromRgb(0xD9, 0x8A, 0x00);
    public static Color Red => Mode == ThemeMode.Dark ? Color.FromRgb(0xFF, 0x5C, 0x5C) : Color.FromRgb(0xD9, 0x3A, 0x3A);

    /// <summary>电量对应的颜色。阈值跟随低电量策略默认值(15%)。</summary>
    public static Color LevelColor(int v)
    {
        int t = LowBatteryPolicy.DefaultThreshold;
        if (v <= t) return Red;
        if (v <= t + 20) return Amber;
        return Green;
    }
}
