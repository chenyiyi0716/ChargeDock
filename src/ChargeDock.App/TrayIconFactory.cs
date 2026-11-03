using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ChargeDock.App;

/// <summary>
/// 托盘图标绘制(GDI+)。画一个电池图标,按电量填充,颜色由调用方按电量算好。
/// level 传 -1 表示还没读到数据。
/// </summary>
public static class TrayIconFactory
{
    public static Bitmap Render(int level, Color color)
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            bool unknown = level < 0;
            int v = unknown ? 0 : Math.Max(0, Math.Min(100, level));
            Color c = unknown ? Color.FromArgb(0x8A, 0x8A, 0x99) : color;

            DrawBattery(g, v, unknown, c);
        }
        return bmp;
    }

    static void DrawBattery(Graphics g, int v, bool unknown, Color c)
    {
        // 电池外壳 + 正极小凸点
        using var bodyPen = new Pen(c, 2.2f);
        using var bodyPath = RoundRect(4f, 9f, 21f, 14f, 3f);
        g.DrawPath(bodyPen, bodyPath);

        using var nubBrush = new SolidBrush(c);
        g.FillRectangle(nubBrush, 26f, 13f, 2.6f, 6f);

        if (unknown)
        {
            // 未知:画一条横线
            using var linePen = new Pen(c, 2.2f);
            g.DrawLine(linePen, 9f, 16f, 20f, 16f);
            return;
        }

        if (v > 0)
        {
            int w = (int)Math.Round(17 * v / 100.0);
            if (w < 1) w = 1;
            using var fillBrush = new SolidBrush(c);
            using var fillPath = RoundRect(6.2f, 11.2f, w, 9.6f, 1.6f);
            g.FillPath(fillBrush, fillPath);
        }
    }

    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
