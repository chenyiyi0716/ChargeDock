using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ChargeDock.Core;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ChargeDock.App;

/// <summary>
/// 悬浮卡片(WPF 壳)。无边框、置顶、可拖动;内容 = 标题 + 设备卡片 + 底部。
/// 只依赖 <see cref="IBatteryReader"/> 的读数快照(<see cref="BatteryReading"/>),不碰设备 API。
/// </summary>
public sealed class Flyout
{
    const double Width = 340;
    const double BarWidth = 128;

    readonly Window window;
    readonly Border card;
    readonly TextBlock titleText;
    TextBlock countText = null!;
    readonly StackPanel bodyPanel;      // 设备视图或设置视图,二选一
    readonly TextBlock footText;
    readonly TextBlock gearText;

    bool showSettings;
    bool userDragged;
    bool anchored;

    /// <summary>主题变化时(切换深浅色)回调,供主程序刷新托盘图标等。</summary>
    public Action? ThemeChanged;

    public bool IsVisible => window.IsVisible;

    /// <summary>卡片根元素(供 --render 渲染到 PNG)。</summary>
    public FrameworkElement Card => card;

    public Flyout()
    {
        window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = Width,
        };

        card = new Border
        {
            Background = new SolidColorBrush(Theme.Bg),
            BorderBrush = new SolidColorBrush(Theme.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18, 14, 18, 12),
        };

        var root = new StackPanel();

        // ---------- 标题栏 ----------
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        titleText = Txt("ChargeDock", 15, Theme.Fg, true);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Cursor = Cursors.SizeAll;
        titleText.MouseLeftButtonDown += (_, _) =>
        {
            try { window.DragMove(); userDragged = true; }
            catch { }
        };
        Grid.SetColumn(titleText, 0);
        header.Children.Add(titleText);

        gearText = Txt("⚙", 15, Theme.Dim, false);
        gearText.Cursor = Cursors.Hand;
        gearText.Padding = new Thickness(5, 0, 5, 0);
        gearText.VerticalAlignment = VerticalAlignment.Center;
        gearText.MouseLeftButtonUp += (_, _) => SetView(!showSettings);
        Grid.SetColumn(gearText, 1);
        header.Children.Add(gearText);

        var closeText = Txt("✕", 13, Theme.Dim, false);
        closeText.Cursor = Cursors.Hand;
        closeText.Padding = new Thickness(5, 0, 0, 0);
        closeText.VerticalAlignment = VerticalAlignment.Center;
        closeText.MouseLeftButtonUp += (_, _) => Hide();
        Grid.SetColumn(closeText, 2);
        header.Children.Add(closeText);

        root.Children.Add(header);
        root.Children.Add(Divider());

        // ---------- 内容区(设备视图 / 设置视图) ----------
        bodyPanel = new StackPanel();
        root.Children.Add(bodyPanel);

        // ---------- 底部 ----------
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        footText = Txt("尚未读取", 11, Theme.Dim, false);
        footText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(footText, 0);
        footer.Children.Add(footText);

        var toSettings = Txt("设置 ›", 11, Theme.Muted, false);
        toSettings.Cursor = Cursors.Hand;
        toSettings.VerticalAlignment = VerticalAlignment.Center;
        toSettings.MouseLeftButtonUp += (_, _) => SetView(true);
        Grid.SetColumn(toSettings, 1);
        footer.Children.Add(toSettings);

        root.Children.Add(footer);
        card.Child = root;
        window.Content = card;

        RebuildBody();

        window.SizeChanged += (_, e) =>
        {
            if (!anchored || userDragged) return;
            var wa = SystemParameters.WorkArea;
            window.Left = wa.Right - Width - 12;
            window.Top = wa.Bottom - e.NewSize.Height - 12;
        };
    }

    public void SetView(bool settings)
    {
        showSettings = settings;
        titleText.Text = settings ? "设置" : "ChargeDock";
        RebuildBody();
    }

    void RebuildBody()
    {
        bodyPanel.Children.Clear();
        if (showSettings) bodyPanel.Children.Add(BuildSettings());
        else bodyPanel.Children.Add(BuildDevicesHeader());
    }

    /// <summary>设备视图:状态行 + 设备卡片(内容在 Render 里填充)。</summary>
    FrameworkElement BuildDevicesHeader()
    {
        var root = new StackPanel();

        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        countText = Txt("等待设备接入", 12, Theme.Muted, false);
        countText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(countText, 0);
        bar.Children.Add(countText);

        var refresh = Txt("↻", 16, Theme.Dim, false);
        refresh.Cursor = Cursors.Hand;
        refresh.Padding = new Thickness(6, 0, 2, 0);
        refresh.VerticalAlignment = VerticalAlignment.Center;
        refresh.MouseLeftButtonUp += (_, _) => RefreshRequested?.Invoke();
        Grid.SetColumn(refresh, 1);
        bar.Children.Add(refresh);
        root.Children.Add(bar);

        // 设备卡片容器
        devicesPanel = new StackPanel();
        root.Children.Add(devicesPanel);

        return root;
    }

    StackPanel? devicesPanel;

    public Action? RefreshRequested;

    /// <summary>按可见读数渲染设备卡片。</summary>
    public void Render(IReadOnlyList<BatteryReading> visible, bool anyLow, string foot)
    {
        if (showSettings) return;

        int n = visible.Count;
        countText.Text = n == 0 ? "等待设备接入" : n + " 台设备";
        footText.Text = foot;

        // 低电量 → 卡片变色(边框 + 标题变红)
        card.BorderBrush = new SolidColorBrush(anyLow ? Theme.Red : Theme.Border);
        titleText.Foreground = new SolidColorBrush(anyLow ? Theme.Red : Theme.Fg);

        if (devicesPanel == null) return;
        devicesPanel.Children.Clear();

        if (n == 0)
        {
            var empty = Txt("暂无已连接的设备", 12, Theme.Dim, false);
            empty.Margin = new Thickness(0, 8, 0, 0);
            devicesPanel.Children.Add(empty);
            return;
        }

        foreach (var r in visible)
        {
            devicesPanel.Children.Add(DeviceSection(r));
            if (r != visible[^1]) devicesPanel.Children.Add(Divider());
        }
    }

    FrameworkElement DeviceSection(BatteryReading r)
    {
        var root = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

        // 设备头:图标 + 名称 + 大百分比
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Color accent = Theme.Green;
        var badge = Badge(r.Kind, accent);
        Grid.SetColumn(badge, 0);
        head.Children.Add(badge);

        var texts = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Txt(r.DisplayName, 14, Theme.Fg, true));
        var sub = Txt(r.Status, 11, Theme.Dim, false);
        sub.Margin = new Thickness(0, 2, 0, 0);
        texts.Children.Add(sub);
        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);

        var bigPct = Txt(BigValue(r), 24, Theme.Muted, false);
        bigPct.Foreground = new SolidColorBrush(Theme.LevelColor(BigLevel(r)));
        bigPct.VerticalAlignment = VerticalAlignment.Center;
        bigPct.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(bigPct, 2);
        head.Children.Add(bigPct);

        root.Children.Add(head);

        // 电量条
        var bars = new StackPanel { Margin = new Thickness(44, 6, 0, 0) };

        if (r.Kind == DeviceKind.Headset)
        {
            if (r.Left.HasValue) bars.Children.Add(BarLine("左", r.Left.Value));
            if (r.Right.HasValue) bars.Children.Add(BarLine("右", r.Right.Value));
            if (r.Case.HasValue) bars.Children.Add(BarLine("仓", r.Case.Value));
        }
        else
        {
            if (r.Battery.HasValue) bars.Children.Add(BarLine("电", r.Battery.Value));
        }

        root.Children.Add(bars);
        return root;
    }

    static int BigLevel(BatteryReading r)
    {
        if (r.Kind == DeviceKind.Headset)
        {
            int min = int.MaxValue;
            if (r.Left.HasValue) min = Math.Min(min, r.Left.Value);
            if (r.Right.HasValue) min = Math.Min(min, r.Right.Value);
            if (r.Case.HasValue) min = Math.Min(min, r.Case.Value);
            return min == int.MaxValue ? 100 : min;
        }
        return r.Battery ?? 100;
    }

    static string BigValue(BatteryReading r)
    {
        if (r.Kind == DeviceKind.Headset)
        {
            int min = BigLevel(r);
            bool any = r.Left.HasValue || r.Right.HasValue || r.Case.HasValue;
            return any ? min + "%" : "—";
        }
        return r.Battery.HasValue ? r.Battery + "%" : "—";
    }

    // ================= 设置视图 =================

    FrameworkElement BuildSettings()
    {
        var root = new StackPanel();

        root.Children.Add(ToggleRow("开机自启", Settings.StartupEnabled, on => Settings.StartupEnabled = on));
        root.Children.Add(ToggleRow("低电量提醒", Settings.NotifyEnabled, on => Settings.NotifyEnabled = on));
        root.Children.Add(ToggleRow("深色主题", Theme.Mode == ThemeMode.Dark, on =>
        {
            Settings.DarkTheme = on;
            Theme.Mode = on ? ThemeMode.Dark : ThemeMode.Light;
            ApplyTheme();
            ThemeChanged?.Invoke();
        }));

        var hint = Txt("低电量阈值:15%(鼠标 / 键盘)", 11, Theme.Dim, false);
        hint.Margin = new Thickness(2, 10, 0, 0);
        root.Children.Add(hint);

        var back = Txt("‹ 返回", 12, Theme.Muted, false);
        back.Margin = new Thickness(2, 12, 0, 0);
        back.Cursor = Cursors.Hand;
        back.MouseLeftButtonUp += (_, _) => SetView(false);
        root.Children.Add(back);

        return root;
    }

    Border ToggleRow(string text, bool initial, Action<bool> onChanged)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var mark = Txt(initial ? "✓" : "○", 12, initial ? Theme.Green : Theme.Dim, false);
        mark.Width = 16;
        mark.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(mark);
        var label = Txt(text, 12, initial ? Theme.Fg : Theme.Muted, false);
        label.Margin = new Thickness(2, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(label);

        var border = new Border
        {
            Padding = new Thickness(10, 7, 12, 7),
            Margin = new Thickness(0, 3, 0, 3),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            Child = panel,
        };

        bool isOn = initial;
        void Refresh()
        {
            mark.Text = isOn ? "✓" : "○";
            mark.Foreground = new SolidColorBrush(isOn ? Theme.Green : Theme.Dim);
            label.Foreground = new SolidColorBrush(isOn ? Theme.Fg : Theme.Muted);
            border.Background = new SolidColorBrush(isOn
                ? Color.FromArgb(0x1F, Theme.Green.R, Theme.Green.G, Theme.Green.B)
                : Colors.Transparent);
        }
        Refresh();

        border.MouseLeftButtonUp += (_, _) =>
        {
            isOn = !isOn;
            Refresh();
            onChanged(isOn);
        };

        return border;
    }

    /// <summary>主题切换后重建整卡(保留当前视图)。</summary>
    public void ApplyTheme()
    {
        card.Background = new SolidColorBrush(Theme.Bg);
        card.BorderBrush = new SolidColorBrush(Theme.Border);
        RebuildBody();
    }

    // ================= 显示 / 隐藏 =================

    public void Show()
    {
        if (!userDragged || !window.IsVisible)
        {
            window.UpdateLayout();
            var wa = SystemParameters.WorkArea;
            double h = window.ActualHeight > 0 ? window.ActualHeight : 300;
            window.Left = wa.Right - Width - 12;
            window.Top = wa.Bottom - h - 12;
        }
        anchored = true;
        window.Show();
        window.Activate();
    }

    public void Hide() => window.Hide();

    public void Toggle()
    {
        if (window.IsVisible) Hide();
        else Show();
    }

    public Dispatcher UiDispatcher => window.Dispatcher;

    // ================= 小工具 =================

    static TextBlock Txt(string text, double size, Color color, bool bold)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = new SolidColorBrush(color),
            TextWrapping = TextWrapping.NoWrap,
        };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        return t;
    }

    static Border Divider()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Theme.Divider),
            Margin = new Thickness(0, 11, 0, 11),
        };
    }

    Grid BarLine(string label, int level)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BarWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lb = Txt(label, 12, Theme.Dim, false);
        lb.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lb, 0);
        grid.Children.Add(lb);

        var barHost = new Grid { Width = BarWidth, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        var track = new Rectangle { Fill = new SolidColorBrush(Theme.Track), RadiusX = 3.5, RadiusY = 3.5 };
        barHost.Children.Add(track);
        Color c = Theme.LevelColor(level);
        var fill = new Rectangle
        {
            Fill = new SolidColorBrush(c),
            RadiusX = 3.5,
            RadiusY = 3.5,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = BarWidth * Math.Max(0, Math.Min(100, level)) / 100.0,
        };
        barHost.Children.Add(fill);
        Grid.SetColumn(barHost, 1);
        grid.Children.Add(barHost);

        var val = Txt(level + "%", 12, c, false);
        val.TextAlignment = TextAlignment.Right;
        val.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(val, 2);
        grid.Children.Add(val);

        return grid;
    }

    static Border Badge(DeviceKind kind, Color color)
    {
        var b = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B)),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var vb = new Viewbox { Width = 18, Height = 18, Stretch = Stretch.Uniform, Child = DrawIcon(kind, color) };
        vb.HorizontalAlignment = HorizontalAlignment.Center;
        vb.VerticalAlignment = VerticalAlignment.Center;
        b.Child = vb;
        return b;
    }

    static Canvas DrawIcon(DeviceKind kind, Color color)
    {
        var c = new Canvas { Width = 24, Height = 24 };
        var stroke = new SolidColorBrush(color);
        var fill = new SolidColorBrush(color);

        if (kind == DeviceKind.Headset)
        {
            var band = new Path
            {
                Data = Geometry.Parse("M4.4,16 L4.4,12 A7.6,7.6 0 0 1 19.6,12 L19.6,16"),
                Stroke = stroke,
                StrokeThickness = 2.1,
            };
            band.StrokeStartLineCap = PenLineCap.Round;
            band.StrokeEndLineCap = PenLineCap.Round;
            c.Children.Add(band);
            c.Children.Add(RoundShape(1.8, 13.4, 5.2, 8.2, 2.6, fill, null, 0));
            c.Children.Add(RoundShape(17.0, 13.4, 5.2, 8.2, 2.6, fill, null, 0));
        }
        else if (kind == DeviceKind.Mouse)
        {
            var body = new Path
            {
                Data = Geometry.Parse(
                    "M12,2.5 C7.6,2.5 5.4,6.4 5.4,10.4 L5.4,15.6 C5.4,19.4 8.1,21.5 12,21.5 " +
                    "C15.9,21.5 18.6,19.4 18.6,15.6 L18.6,10.4 C18.6,6.4 16.4,2.5 12,2.5 Z"),
                Stroke = stroke,
                StrokeThickness = 1.8,
            };
            body.StrokeLineJoin = PenLineJoin.Round;
            c.Children.Add(body);
            c.Children.Add(RoundShape(10.8, 5.5, 2.4, 4.2, 1.2, fill, null, 0));
        }
        else // keyboard
        {
            c.Children.Add(RoundShape(2.4, 6.6, 19.2, 10.8, 2.2, null, stroke, 1.9));
            for (int i = 0; i < 4; i++)
                c.Children.Add(RoundShape(5.4 + i * 3.8, 9.4, 1.9, 1.9, 0.6, fill, null, 0));
            c.Children.Add(RoundShape(7.6, 13.4, 8.8, 1.8, 0.7, fill, null, 0));
        }

        return c;
    }

    static Rectangle RoundShape(double x, double y, double w, double h, double r, Brush? fill, Brush? stroke, double thickness)
    {
        var rect = new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        return rect;
    }
}
