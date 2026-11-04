using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ChargeDock.Core;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ChargeDock.App;

/// <summary>
/// 玻璃风格的托盘右键菜单(自绘,替换系统 ContextMenuStrip)。
/// 图标分组式、无状态头;含 显示电量 / 立即刷新 / 设置 / 开机自启(开关) / 关于 / 退出(危险色)。
/// </summary>
public sealed class TrayMenu
{
    const double MenuWidth = 250;
    const double Radius = 16;

    readonly Window window;
    readonly Border card;
    readonly StackPanel panel;
    readonly Color danger = Color.FromRgb(0xDC, 0x26, 0x26);
    TextBlock? showHint;
    ToggleItem? startupToggle;
    bool built;
    int lastLevel = -1;

    public Action? ShowCardRequested;
    public Action? RefreshRequested;
    public Action? SettingsRequested;
    public Action? AboutRequested;
    public Action? ExitRequested;

    public bool IsVisible => window.IsVisible;

    public TrayMenu()
    {
        window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
        };

        System.Windows.Shell.WindowChrome.SetWindowChrome(window, new System.Windows.Shell.WindowChrome
        {
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(0),
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false,
        });

        card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Background = Brushes.Transparent,
        };
        panel = new StackPanel { Width = MenuWidth };
        card.Child = panel;
        window.Content = card;

        window.SourceInitialized += (_, _) => ApplyGlass();
        window.Deactivated += (_, _) => Hide();
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
    }

    void ApplyGlass()
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        GlassEffect.ApplySystemAcrylic(hwnd, Theme.IsDark);
        GlassEffect.ApplyNativeRoundedCorners(hwnd);
    }

    void Build()
    {
        panel.Children.Clear();
        card.Background = Brushes.Transparent;

        // 显示电量(+ 最低电量提示)
        showHint = Txt(lastLevel < 0 ? "—" : lastLevel + "%", 11, Theme.Muted, false);
        panel.Children.Add(Item("显示电量", "card", showHint, false, OnShowCard));

        // 立即刷新
        panel.Children.Add(Item("立即刷新", "refresh", null, false, OnRefresh));

        panel.Children.Add(Separator());

        // 设置…
        panel.Children.Add(Item("设置…", "gear", null, false, OnSettings));

        // 开机自启(内置开关)
        startupToggle = new ToggleItem(Settings.StartupEnabled, v =>
        {
            Settings.StartupEnabled = v;
            startupToggle!.Set(v);
        });
        panel.Children.Add(Item("开机自启", "power", startupToggle.Root, false, null));

        panel.Children.Add(Separator());

        // 关于
        panel.Children.Add(Item("关于 ChargeDock", "info", Txt("v0.1.0", 11, Theme.Muted, false), false, OnAbout));

        // 退出(危险色)
        panel.Children.Add(Item("退出", "exit", null, true, OnExit, danger));

        built = true;
    }

    public void SetBattery(int level)
    {
        lastLevel = level;
        if (!built) Build();
        if (showHint != null) showHint.Text = level < 0 ? "—" : level + "%";
    }

    public void ShowAt(int screenX, int screenY)
    {
        Build();
        startupToggle?.Set(Settings.StartupEnabled);

        // 首次显示时窗口尚未布局(ActualWidth/Height 为 0),先测量内容拿到真实尺寸,
        // 否则会按兜底尺寸定位,导致第一次弹出的位置偏离鼠标。
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = panel.DesiredSize.Width + 12;
        double h = panel.DesiredSize.Height + 12;

        var wa = SystemParameters.WorkArea;
        double left = screenX - w;
        double top = screenY - h;
        if (left < wa.Left) left = wa.Left;
        if (top < wa.Top) top = wa.Top;
        if (left + w > wa.Right) left = wa.Right - w;
        if (top + h > wa.Bottom) top = wa.Bottom - h;

        window.Left = left;
        window.Top = top;
        window.Show();
        window.Activate();
    }

    public void Hide() => window.Hide();

    // ================= 动作 =================

    void OnShowCard() => ShowCardRequested?.Invoke();
    void OnRefresh() => RefreshRequested?.Invoke();
    void OnSettings() => SettingsRequested?.Invoke();
    void OnAbout() => AboutRequested?.Invoke();
    void OnExit() => ExitRequested?.Invoke();

    // ================= 构造小工具 =================

    FrameworkElement Item(string label, string icon, FrameworkElement? trailing, bool danger2, Action? onClick, Color? color = null)
    {
        var fg = color ?? Theme.Fg;
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBox = new Viewbox { Width = 16, Height = 16, Stretch = Stretch.Uniform, Child = DrawIcon(icon, fg) };
        iconBox.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(iconBox, 0);
        row.Children.Add(iconBox);

        var lb = Txt(label, 13, fg, false);
        lb.Margin = new Thickness(10, 0, 0, 0);
        lb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lb, 1);
        row.Children.Add(lb);

        if (trailing != null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            row.Children.Add(trailing);
        }

        var b = new Border
        {
            Child = row,
            Padding = new Thickness(11, 8, 11, 8),
            CornerRadius = new CornerRadius(9),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
        };

        b.MouseEnter += (_, _) => b.Background = new SolidColorBrush(danger2
            ? Color.FromArgb(0x1A, 0xDC, 0x26, 0x26)
            : Theme.HoverBg);
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;

        if (onClick != null)
            b.MouseLeftButtonUp += (_, _) => { Hide(); onClick(); };

        return b;
    }

    Border Separator() => new()
    {
        Height = 1,
        Background = new SolidColorBrush(Theme.Divider),
        Margin = new Thickness(10, 4, 10, 4),
    };

    static TextBlock Txt(string text, double size, Color color, bool bold)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = new SolidColorBrush(color),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
        };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        return t;
    }

    // ================= 菜单图标(24×24 线形) =================

    static Canvas DrawIcon(string kind, Color color)
    {
        var c = new Canvas { Width = 24, Height = 24 };
        var stroke = new SolidColorBrush(color);

        switch (kind)
        {
            case "card":
                c.Children.Add(RoundRect(3, 4, 18, 15, 2.5, null, stroke, 1.7));
                c.Children.Add(Line(3, 9, 21, 9, stroke, 1.7));
                c.Children.Add(Line(7, 12.5, 13, 12.5, stroke, 1.7));
                break;
            case "refresh":
                c.Children.Add(Arc("M20,11 A8,8 0 1 0 18,17", stroke, 1.7));
                c.Children.Add(Arc("M20,4 L20,11 L13,11", stroke, 1.7));
                break;
            case "gear":
                c.Children.Add(new Ellipse { Width = 6, Height = 6, Stroke = stroke, StrokeThickness = 1.7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                c.Children.Add(Line(12, 2.5, 12, 4.5, stroke, 1.7));
                c.Children.Add(Line(12, 19.5, 12, 21.5, stroke, 1.7));
                c.Children.Add(Line(2.5, 12, 4.5, 12, stroke, 1.7));
                c.Children.Add(Line(19.5, 12, 21.5, 12, stroke, 1.7));
                c.Children.Add(Line(4.6, 4.6, 6, 6, stroke, 1.7));
                c.Children.Add(Line(18, 18, 19.4, 19.4, stroke, 1.7));
                c.Children.Add(Line(4.6, 19.4, 6, 18, stroke, 1.7));
                c.Children.Add(Line(18, 6, 19.4, 4.6, stroke, 1.7));
                break;
            case "power":
                c.Children.Add(Line(12, 3, 12, 11, stroke, 1.7));
                c.Children.Add(Arc("M6.5,6.4 A8,8 0 1 0 17.5,6.4", stroke, 1.7));
                break;
            case "info":
                c.Children.Add(new Ellipse { Width = 18, Height = 18, Stroke = stroke, StrokeThickness = 1.7, Margin = new Thickness(3, 3, 0, 0) });
                c.Children.Add(Line(12, 11, 12, 16, stroke, 1.7));
                c.Children.Add(new Ellipse { Width = 1.6, Height = 1.6, Fill = stroke, Margin = new Thickness(11.2, 7.2, 0, 0) });
                break;
            default: // exit
                c.Children.Add(Arc("M9,4 L5,4 A2,2 0 0 0 3,6 L3,18 A2,2 0 0 0 5,20 L9,20", stroke, 1.7));
                c.Children.Add(Arc("M16,17 L21,12 L16,7", stroke, 1.7));
                c.Children.Add(Line(21, 12, 9, 12, stroke, 1.7));
                break;
        }
        return c;
    }

    static Path Arc(string data, Brush stroke, double thickness) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
    };

    static Rectangle RoundRect(double x, double y, double w, double h, double r, Brush? fill, Brush? stroke, double thickness)
    {
        var rect = new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        return rect;
    }

    static Line Line(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
    {
        var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness };
        l.StrokeStartLineCap = PenLineCap.Round;
        l.StrokeEndLineCap = PenLineCap.Round;
        return l;
    }

    /// <summary>菜单内置的小开关(开机自启)。</summary>
    sealed class ToggleItem
    {
        public Border Root = null!;
        readonly Border knob;
        readonly TranslateTransform shift;
        bool on;

        public ToggleItem(bool initial, Action<bool> onChanged)
        {
            on = initial;
            Root = new Border
            {
                Width = 34,
                Height = 20,
                CornerRadius = new CornerRadius(99),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(on ? ToggleOn : Theme.ToggleOff),
            };
            knob = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
            };
            shift = new TranslateTransform(on ? 16 : 0, 0);
            knob.RenderTransform = shift;
            Root.Child = knob;

            Root.MouseLeftButtonUp += (_, _) =>
            {
                on = !on;
                Root.Background = new SolidColorBrush(on ? ToggleOn : Theme.ToggleOff);
                shift.X = on ? 16 : 0;
                onChanged(on);
            };
        }

        public void Set(bool value)
        {
            on = value;
            Root.Background = new SolidColorBrush(on ? ToggleOn : Theme.ToggleOff);
            shift.X = on ? 16 : 0;
        }

        static Color ToggleOn => Color.FromRgb(0x2E, 0x5D, 0x4E);
    }
}
