using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using ChargeDock.Core;

namespace ChargeDock.App;

/// <summary>
/// 托盘图标绘制(GDI+):环形仪表 —— 环形进度,弧长表示电量。
/// 颜色随电量(绿 / 黄 / 红);轨道色随系统深浅色。level 传 -1 表示未知。
/// </summary>
public static class TrayIconFactory
{
    public static Bitmap Render(int level, int size = 32)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        bool unknown = level < 0;
        int v = unknown ? 0 : Math.Clamp(level, 0, 100);
        Color arc = unknown ? Color.FromArgb(0x8A, 0x8A, 0x99) : LevelColor(level);
        Color track = Theme.SystemIsDark ? Color.FromArgb(0x2F, 0x35, 0x3D) : Color.FromArgb(0xDD, 0xE1, 0xE6);

        // 设计稿 viewBox 100×100:r=42,stroke=9,按实际尺寸等比缩放
        float scale = size / 100f;
        float r = 42 * scale;
        float sw = 9 * scale;
        float cx = 50 * scale;
        float cy = 50 * scale;
        var rect = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);

        using (var trackPen = new Pen(track, sw))
            g.DrawEllipse(trackPen, rect);

        if (!unknown && v > 0)
        {
            using var arcPen = new Pen(arc, sw) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arcPen, rect, -90, 360f * v / 100f);
        }

        return bmp;
    }

    static Color LevelColor(int level) => BatteryColorPolicy.Classify(level) switch
    {
        BatteryLevelClass.Mid => Color.FromArgb(0xF5, 0x9E, 0x0B),
        BatteryLevelClass.Low => Color.FromArgb(0xEF, 0x44, 0x44),
        _ => Color.FromArgb(0x22, 0xC5, 0x5E),
    };
}
