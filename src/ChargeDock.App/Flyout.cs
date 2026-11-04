using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ChargeDock.Core;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ChargeDock.App;

/// <summary>
/// 玻璃拟态悬浮卡片(重写版)。半透明毛玻璃、22px 大圆角、柔和描边与弥散阴影;
/// 主卡片 = 标题栏 + 设备列表,设置页 = 通用 / 低电量提醒 / 外观 / 关于 四组。
/// 含动效:入场淡入上滑、退出淡出、电量条 ScaleX 平滑过渡、开关滑块、切页淡入。
/// </summary>
public sealed class Flyout
{
    const double CardWidth = 320;
    const double ShadowMargin = 18;

    /// <summary>--render 模式置 false,避免动画停在中间态。</summary>
    public static bool AnimationsEnabled = true;

    readonly Window window;
    readonly Border card;
    readonly StackPanel bodyPanel;
    StackPanel root = null!;
    TextBlock titleText = null!;
    StackPanel statusPanel = null!;
    TextBlock statusText = null!;
    Ellipse statusDot = null!;
    Border gearBtn = null!;
    Border backBtn = null!;
    Border closeBtn = null!;
    Border? headerDivider;
    bool showSettings;
    /// <summary>最近一次读数缓存:视图重建(主题切换/视图切换)后回填,避免数据空白。</summary>
    List<BatteryReading> lastReadings = new();
    /// <summary>设置面板窗口(独立窗口,从卡片左侧弹出,不覆盖卡片)。</summary>
    Window? settingsWindow;

    // 设备行(持久化,更新时不重建,保证电量条动画)
    DeviceRowView? mouseRow, keyboardRow, headsetRow;

    public bool IsVisible => window.IsVisible;
    public FrameworkElement Card => card;
    public Dispatcher UiDispatcher => window.Dispatcher;

    /// <summary>任一设置改变时触发(供主程序刷新定时器 / 托盘 / 主题)。</summary>
    public Action? SettingsChanged;
    public Action? RefreshRequested;

    public Flyout()
    {
        // 分层窗口:圆角与玻璃底由卡片自己绘制,整张卡可以 GPU 合成滑动。
        window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Manual,
            Width = CardWidth,
            Height = 340,
        };

        card = new Border
        {
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(20),
            // 底部留 8px:窗口延伸到任务栏上沿,卡片下移时由窗口底边裁掉
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.30, Color = Colors.Black },
        };

        root = new StackPanel();
        root.Children.Add(BuildHeader());

        headerDivider = new Border { Height = 1, Background = new SolidColorBrush(Theme.Divider), Margin = new Thickness(0, 14, 0, 0) };
        root.Children.Add(headerDivider);

        bodyPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        root.Children.Add(bodyPanel);

        card.Child = root;
        window.Content = card;

        ApplyShellColors();
        SetView(false, animate: false);

        // 内容面板尺寸变化 = 卡片内容高度变化(卡片本身被窗口拉伸,card.SizeChanged 不会触发)
        bodyPanel.SizeChanged += (_, _) => ApplyCardSize(animate: true);
    }

    void ApplyShellColors()
    {
        if (card == null) return;
        card.Background = new SolidColorBrush(Theme.CardBg);
        card.BorderBrush = new SolidColorBrush(Theme.CardBorder);
    }

    double targetCardHeight = -1;
    bool sizingCard;

    /// <summary>内容尺寸变化时,窗口高度做平滑过渡(底边固定,向上/向下伸缩)。</summary>
    void ApplyCardSize(bool animate)
    {
        if (card == null || sizingCard) return;
        sizingCard = true;
        try
        {
            // 卡片本身被窗口拉伸,DesiredSize 不可靠;测内容根节点的自然高度
            root.Measure(new Size(CardWidth - 40, double.PositiveInfinity));
            double target = root.DesiredSize.Height + 48;
            if (target <= 0) return;
            if (Math.Abs(target - targetCardHeight) < 0.5) return;
            targetCardHeight = target;

            var wa = SystemParameters.WorkArea;
            window.Left = wa.Right - window.Width - 8;

            if (!animate || !AnimationsEnabled || !window.IsVisible || window.ActualHeight <= 0)
            {
                window.BeginAnimation(FrameworkElement.HeightProperty, null);
                window.BeginAnimation(Window.TopProperty, null);
                window.Height = target;
                window.Top = wa.Bottom - target;
                return;
            }

            double fromHeight = window.ActualHeight;
            var dur = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            window.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(fromHeight, target, dur) { EasingFunction = ease });
            window.BeginAnimation(Window.TopProperty, new DoubleAnimation(wa.Bottom - fromHeight, wa.Bottom - target, dur) { EasingFunction = ease });
        }
        finally
        {
            sizingCard = false;
        }
    }

    /// <summary>设置面板:从卡片左侧弹出(独立窗口,不覆盖卡片)。</summary>
    public void ShowSettings()
    {
        const double panelWidth = 300;

        var glass = new Border
        {
            Width = panelWidth,
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(20),
            Background = new SolidColorBrush(Theme.CardBg),
            BorderBrush = new SolidColorBrush(Theme.CardBorder),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 0, Opacity = 0.30, Color = Colors.Black },
        };

        var panel = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Txt("设置", 15, Theme.Fg, true);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0);
        head.Children.Add(title);

        var close = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Theme.IconBg),
            Cursor = Cursors.Hand,
        };
        var cvb = new Viewbox
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            Child = DrawClose(Theme.IconFg),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        close.Child = cvb;
        close.MouseLeftButtonUp += (_, _) => HideSettings();
        Grid.SetColumn(close, 1);
        head.Children.Add(close);
        panel.Children.Add(head);

        panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Theme.Divider), Margin = new Thickness(0, 14, 0, 0) });

        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(BuildSettingsView());
        panel.Children.Add(body);

        glass.Child = panel;

        if (settingsWindow == null)
        {
            settingsWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
            };
            settingsWindow.Deactivated += (_, _) => HideSettings();
            settingsWindow.KeyDown += (_, e) => { if (e.Key == Key.Escape) HideSettings(); };
        }
        settingsWindow.Content = glass;

        // 位置:面板右边 = 卡片左边 - 8,底边与卡片底边对齐,向上展开
        glass.Measure(new Size(panelWidth, double.PositiveInfinity));
        double panelHeight = glass.DesiredSize.Height;
        double cardBottom = window.Top + window.ActualHeight;
        settingsWindow.Left = window.Left - 8 - panelWidth;
        settingsWindow.Top = Math.Max(SystemParameters.WorkArea.Top, cardBottom - panelHeight);

        // 仿照卡片:内容从下方滑入(窗口底边裁切)
        if (glass.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform(0, 0);
            glass.RenderTransform = shift;
        }
        shift.BeginAnimation(TranslateTransform.YProperty, null);

        if (!AnimationsEnabled)
        {
            shift.Y = 0;
            settingsWindow.Show();
            settingsWindow.Activate();
            return;
        }

        shift.Y = 16;
        settingsWindow.Show();
        settingsWindow.Activate();

        var slide = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>收起设置面板(带下滑动画)。</summary>
    public void HideSettings()
    {
        if (settingsWindow == null || !settingsWindow.IsVisible) return;

        if (!AnimationsEnabled
            || settingsWindow.Content is not Border glass
            || glass.RenderTransform is not TranslateTransform shift)
        {
            settingsWindow.Hide();
            return;
        }

        var slide = new DoubleAnimation(shift.Y, 16, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        slide.Completed += (_, _) => settingsWindow.Hide();
        shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    /// <summary>--render 用:亚克力只在真实窗口生效,渲染时给卡片一个可见底色。</summary>
    public void ApplyRenderFallback() => card.Background = new SolidColorBrush(Theme.CardBg);

    /// <summary>--render-settings 用:切到设置视图。</summary>
    public void ShowSettingsForRender() => SetView(true, animate: false);

    // ================= 标题栏 =================

    FrameworkElement BuildHeader()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        titleText = Txt("ChargeDock", 15, Theme.Fg, true);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(titleText, 0);
        header.Children.Add(titleText);

        // 状态:N 已连接
        statusPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        statusDot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Fill = new SolidColorBrush(Theme.StatusDot) };
        statusPanel.Children.Add(statusDot);
        statusText = Txt("0 已连接", 11, Theme.Muted, false);
        statusText.Margin = new Thickness(5, 0, 0, 0);
        statusText.VerticalAlignment = VerticalAlignment.Center;
        statusPanel.Children.Add(statusText);
        Grid.SetColumn(statusPanel, 1);
        header.Children.Add(statusPanel);

        // 齿轮(打开设置)
        gearBtn = HeaderButton(DrawGear(Theme.IconFg));
        gearBtn.Margin = new Thickness(0, 0, 8, 0);
        gearBtn.MouseLeftButtonUp += (_, _) => SetView(true);
        Grid.SetColumn(gearBtn, 2);
        header.Children.Add(gearBtn);

        // 返回按钮(设置视图用,占齿轮的位置)
        backBtn = HeaderButton(DrawBack(Theme.IconFg));
        backBtn.Margin = new Thickness(0, 0, 8, 0);
        backBtn.Visibility = Visibility.Collapsed;
        backBtn.MouseLeftButtonUp += (_, _) => SetView(false);
        Grid.SetColumn(backBtn, 2);
        header.Children.Add(backBtn);

        // 关闭(卡片视图 → 收起;设置视图 → 返回卡片)
        closeBtn = HeaderButton(DrawClose(Theme.IconFg));
        closeBtn.MouseLeftButtonUp += (_, _) =>
        {
            if (showSettings) SetView(false);
            else Hide();
        };
        Grid.SetColumn(closeBtn, 3);
        header.Children.Add(closeBtn);

        return header;
    }

    Border HeaderButton(Canvas icon)
    {
        var b = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Theme.IconBg),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var vb = new Viewbox { Width = 16, Height = 16, Stretch = Stretch.Uniform, Child = icon };
        vb.HorizontalAlignment = HorizontalAlignment.Center;
        vb.VerticalAlignment = VerticalAlignment.Center;
        b.Child = vb;
        return b;
    }

    static Canvas DrawClose(Color color)
    {
        var c = new Canvas { Width = 24, Height = 24 };
        var stroke = new SolidColorBrush(color);
        c.Children.Add(Line(6, 6, 18, 18, stroke, 2));
        c.Children.Add(Line(18, 6, 6, 18, stroke, 2));
        return c;
    }

    // ================= 视图切换 =================

    public void SetView(bool settings) => SetView(settings, animate: true);

    void SetView(bool settings, bool animate)
    {
        showSettings = settings;
        titleText.Text = settings ? "设置" : "ChargeDock";
        // 设置视图:隐藏「N 已连接」与齿轮,只留关闭(返回卡片)
        if (statusPanel != null) statusPanel.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        if (gearBtn != null) gearBtn.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        if (backBtn != null) backBtn.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;

        var content = settings ? BuildSettingsView() : BuildDevicesView();
        bodyPanel.Children.Clear();
        bodyPanel.Children.Add(content);

        // 重建后回填当前读数(主题切换 / 视图切换不会丢数据)
        if (!settings) ApplyReadings(lastReadings);

        if (animate && AnimationsEnabled)
        {
            // 页面切换:设置页从右侧滑入,设备页从左侧滑入,配合淡入
            double offset = settings ? 22 : -22;
            var tt = new TranslateTransform(offset, 0);
            content.RenderTransform = tt;
            content.Opacity = 0;
            var dur = TimeSpan.FromMilliseconds(200);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(offset, 0, dur) { EasingFunction = ease });
        }
    }

    // ================= 设备视图 =================

    FrameworkElement BuildDevicesView()
    {
        var panel = new StackPanel();
        mouseRow = AddDeviceRow(panel, DeviceKind.Mouse, isFirst: true);
        keyboardRow = AddDeviceRow(panel, DeviceKind.Keyboard, isFirst: false);
        headsetRow = AddDeviceRow(panel, DeviceKind.Headset, isFirst: false);
        return panel;
    }

    DeviceRowView AddDeviceRow(StackPanel panel, DeviceKind kind, bool isFirst)
    {
        var row = new DeviceRowView();
        var meta = MetaFor(kind);
        row.Name = Txt(NameFor(kind), 13.5, Theme.Fg, true);
        row.Meta = Txt(meta, 10.5, Theme.Dim, false);

        var body = new StackPanel();
        body.Children.Add(row.Name);
        row.Meta.Margin = new Thickness(0, 1, 0, 0);
        body.Children.Add(row.Meta);

        if (kind == DeviceKind.Headset)
        {
            var ears = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            for (int i = 0; i < 3; i++)
                ears.Children.Add(BuildEarRow(row, i == 0 ? "L" : i == 1 ? "R" : "仓"));
            body.Children.Add(ears);
        }
        else
        {
            row.Bar = new Bar();
            row.Bar.Root.Margin = new Thickness(0, 7, 0, 0);
            body.Children.Add(row.Bar.Root);
        }

        var grid = new Grid { Margin = new Thickness(0, isFirst ? 0 : 11, 0, 11) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.Icon = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Theme.IconBg),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var ivb = new Viewbox { Width = 24, Height = 24, Stretch = Stretch.Uniform, Child = DrawDeviceIcon(kind, Theme.IconFg) };
        ivb.HorizontalAlignment = HorizontalAlignment.Center;
        ivb.VerticalAlignment = VerticalAlignment.Center;
        row.Icon.Child = ivb;
        Grid.SetColumn(row.Icon, 0);
        grid.Children.Add(row.Icon);

        body.Margin = new Thickness(11, 0, 0, 0);
        body.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        if (kind != DeviceKind.Headset)
        {
            row.Pct = Txt("—", 14, Theme.Fg, true);
            row.Pct.MinWidth = 38;
            row.Pct.TextAlignment = TextAlignment.Right;
            row.Pct.VerticalAlignment = VerticalAlignment.Center;
            row.Pct.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(row.Pct, 2);
            grid.Children.Add(row.Pct);
        }

        row.Root = grid;
        panel.Children.Add(grid);
        return row;
    }

    FrameworkElement BuildEarRow(DeviceRowView parent, string label)
    {
        var ear = new EarRow();
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        var k = Txt(label, 10, Theme.Muted, true);
        k.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(k, 0);
        grid.Children.Add(k);

        ear.Bar = new Bar();
        Grid.SetColumn(ear.Bar.Root, 1);
        grid.Children.Add(ear.Bar.Root);

        ear.Pct = Txt("—", 11.5, Theme.Fg, true);
        ear.Pct.TextAlignment = TextAlignment.Right;
        ear.Pct.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(ear.Pct, 2);
        grid.Children.Add(ear.Pct);

        ear.Root = grid;
        parent.Ears.Add(ear);
        return grid;
    }

    /// <summary>按可见读数更新设备行(不重建,保证电量条动画)。</summary>
    public void Render(IReadOnlyList<BatteryReading> visible)
    {
        lastReadings = new List<BatteryReading>(visible);
        if (showSettings) return;
        ApplyReadings(lastReadings);
    }

    void ApplyReadings(IReadOnlyList<BatteryReading> visible)
    {
        int n = visible.Count;
        statusText.Text = n == 0 ? "0 已连接" : n + " 已连接";

        // 先全部隐藏
        foreach (var row in new[] { mouseRow, keyboardRow, headsetRow })
            if (row != null) row.Root.Visibility = Visibility.Collapsed;

        foreach (var r in visible)
        {
            var row = r.Kind switch
            {
                DeviceKind.Mouse => mouseRow,
                DeviceKind.Keyboard => keyboardRow,
                _ => headsetRow,
            };
            if (row == null) continue;

            row.Root.Visibility = Visibility.Visible;
            row.Name.Text = r.DisplayName;

            if (r.Kind == DeviceKind.Headset)
            {
                UpdateEar(row.Ears[0], r.Left);
                UpdateEar(row.Ears[1], r.Right);
                UpdateEar(row.Ears[2], r.Case);
            }
            else
            {
                int level = r.Battery ?? -1;
                if (level >= 0)
                {
                    row.Bar!.Set(level);
                    row.Pct!.Text = level + "%";
                    row.Pct.Foreground = new SolidColorBrush(Theme.LevelTextColor(level));
                }
                else
                {
                    row.Bar!.Set(0);
                    row.Pct!.Text = "—";
                    row.Pct.Foreground = new SolidColorBrush(Theme.Dim);
                }
            }

            // 设计稿:低电量只让电量条/百分比变红,设备名与图标保持常规配色
            row.Name.Foreground = new SolidColorBrush(Theme.Fg);
            row.Icon.Background = new SolidColorBrush(Theme.IconBg);
        }
    }

    void UpdateEar(EarRow ear, int? level)
    {
        if (level.HasValue)
        {
            ear.Root.Visibility = Visibility.Visible;
            ear.Bar.Set(level.Value);
            ear.Pct.Text = level + "%";
            ear.Pct.Foreground = new SolidColorBrush(Theme.LevelTextColor(level.Value));
        }
        else
        {
            ear.Root.Visibility = Visibility.Collapsed;
        }
    }

    // ================= 设置视图 =================

    FrameworkElement BuildSettingsView()
    {
        var root = new StackPanel();

        // 通用
        root.Children.Add(GroupTitle("通用"));
        root.Children.Add(ToggleRow("开机自启", Settings.StartupEnabled, v =>
        {
            Settings.StartupEnabled = v;
            SettingsChanged?.Invoke();
        }));

        // 低电量提醒(阈值固定 15%,鼠标 / 键盘)
        root.Children.Add(GroupTitle("低电量提醒 · 鼠标 / 键盘"));
        root.Children.Add(ToggleRow("弹窗提醒", Settings.PopupNotify, v =>
        {
            Settings.PopupNotify = v;
            SettingsChanged?.Invoke();
        }));

        // 外观
        root.Children.Add(GroupTitle("外观"));
        root.Children.Add(SegRow("主题",
            new[] { "浅色", "深色", "跟随系统" },
            ThemeModeIndex(Theme.Mode),
            i =>
            {
                var modes = new[] { ThemeMode.Light, ThemeMode.Dark, ThemeMode.System };
                Theme.Mode = modes[i];
                Settings.ThemeMode = Theme.Mode switch
                {
                    ThemeMode.Light => "light",
                    ThemeMode.Dark => "dark",
                    _ => "system",
                };
                ApplyTheme();
                SettingsChanged?.Invoke();
            }));

        // 关于
        root.Children.Add(GroupTitle("关于"));
        root.Children.Add(AboutRow());

        return root;
    }

    FrameworkElement AboutRow()
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lab = new StackPanel();
        var name = Txt("ChargeDock", 13, Theme.Fg, false);
        var ver = Txt("版本 0.1.0", 10.5, Theme.Dim, false);
        ver.Margin = new Thickness(0, 1, 0, 0);
        lab.Children.Add(name);
        lab.Children.Add(ver);
        Grid.SetColumn(lab, 0);
        grid.Children.Add(lab);

        var check = Txt("检查更新", 12, Theme.Muted, false);
        check.Cursor = Cursors.Hand;
        check.VerticalAlignment = VerticalAlignment.Center;
        check.MouseLeftButtonUp += (_, _) => { check.Text = "已是最新版本"; };
        Grid.SetColumn(check, 1);
        grid.Children.Add(check);

        return grid;
    }

    // ================= 控件 =================

    TextBlock GroupTitle(string text)
    {
        var t = Txt(text, 10, Theme.Muted, true);
        t.Margin = new Thickness(0, 14, 0, 2);
        return t;
    }

    FrameworkElement ToggleRow(string label, bool initial, Action<bool> onChanged)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 0), MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lab = Txt(label, 13, Theme.Fg, false);
        lab.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lab, 0);
        grid.Children.Add(lab);

        var toggle = new GlassToggle(initial, onChanged);
        Grid.SetColumn(toggle.Root, 1);
        grid.Children.Add(toggle.Root);

        return grid;
    }

    FrameworkElement SegRow(string label, string[] options, int selected, Action<int> onChanged)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 0), MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lab = Txt(label, 13, Theme.Fg, false);
        lab.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lab, 0);
        grid.Children.Add(lab);

        var seg = new SegmentedControl(options, selected, onChanged);
        Grid.SetColumn(seg.Root, 1);
        grid.Children.Add(seg.Root);

        return grid;
    }

    FrameworkElement StepperRow(string label, int value, int min, int max, Action<int> onChanged)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 0), MinHeight = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lab = Txt(label, 13, Theme.Fg, false);
        lab.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lab, 0);
        grid.Children.Add(lab);

        var stepper = new Stepper(value, min, max, onChanged);
        Grid.SetColumn(stepper.Root, 1);
        grid.Children.Add(stepper.Root);

        return grid;
    }

    // ================= 显示 / 隐藏(动效) =================

    /// <summary>卡片已显示时不重复弹出(用于托盘菜单「显示电量」,避免把卡片收起来)。</summary>
    public void ShowIfHidden()
    {
        if (!window.IsVisible) Show();
    }

    public void Show()
    {
        // 首帧窗口尚未布局,测量内容根节点拿到自然高度
        root.Measure(new Size(CardWidth - 40, double.PositiveInfinity));
        double h = root.DesiredSize.Height > 0 ? root.DesiredSize.Height + 48 : 340;
        targetCardHeight = h;

        var wa = SystemParameters.WorkArea;
        // h 已包含卡片底部 8px 边距,窗口底边正好落在任务栏上沿
        window.BeginAnimation(FrameworkElement.HeightProperty, null);
        window.BeginAnimation(Window.TopProperty, null);
        window.Height = h;
        window.Left = wa.Right - window.Width - 8;
        window.Top = wa.Bottom - h;
        window.Opacity = 1;
        window.Show();
        window.Activate();

        window.UpdateLayout();

        if (!AnimationsEnabled)
        {
            if (card.RenderTransform is TranslateTransform t0) t0.Y = 0;
            return;
        }

        // 整卡向上平移(从窗口底边升起;下移时由窗口底边=任务栏上沿裁掉)
        double travel = Math.Max(8, card.ActualHeight + 8);
        if (card.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform(0, 0);
            card.RenderTransform = shift;
        }
        shift.Y = travel;
        var slide = new DoubleAnimation(travel, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    public void Hide()
    {
        HideSettings();
        if (!window.IsVisible) return;

        if (!AnimationsEnabled || card.RenderTransform is not TranslateTransform shift)
        {
            window.Hide();
            return;
        }

        // 整卡向下平移,越过任务栏上沿的部分被窗口底边裁掉(看起来沉进任务栏)
        double travel = Math.Max(8, card.ActualHeight + 8);
        var slide = new DoubleAnimation(shift.Y, travel, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        slide.Completed += (_, _) => window.Hide();
        shift.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    public void Toggle()
    {
        if (window.IsVisible) Hide();
        else Show();
    }

    /// <summary>主题切换:淡出旧内容 → 换主题 → 淡入新内容,形成平滑过渡。</summary>
    public void ApplyTheme()
    {
        HideSettings();
        if (AnimationsEnabled && window.IsVisible)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOut.Completed += (_, _) =>
            {
                DoApplyTheme();
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                root.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            };
            root.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        else
        {
            root.Opacity = 1;
            DoApplyTheme();
        }
    }

    void DoApplyTheme()
    {
        ApplyShellColors();
        titleText.Foreground = new SolidColorBrush(Theme.Fg);
        statusText.Foreground = new SolidColorBrush(Theme.Muted);
        statusDot.Fill = new SolidColorBrush(Theme.StatusDot);
        if (headerDivider != null) headerDivider.Background = new SolidColorBrush(Theme.Divider);
        if (gearBtn != null)
        {
            gearBtn.Background = new SolidColorBrush(Theme.IconBg);
            if (gearBtn.Child is Viewbox gv) gv.Child = DrawGear(Theme.IconFg);
        }
        if (closeBtn != null)
        {
            closeBtn.Background = new SolidColorBrush(Theme.IconBg);
            if (closeBtn.Child is Viewbox cv) cv.Child = DrawClose(Theme.IconFg);
        }
        SetView(showSettings, animate: false);
    }

    // ================= 小工具 =================

    static TextBlock Txt(string text, double size, Color color, bool bold)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = new SolidColorBrush(color),
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
        };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        return t;
    }

    static string NameFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "HECATE G3M Pro",
        DeviceKind.Keyboard => "K820",
        _ => "REDMI Buds 8 Pro",
    };

    static string MetaFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "鼠标 · 2.4G",
        DeviceKind.Keyboard => "键盘 · 2.4G",
        _ => "耳机 · 蓝牙",
    };

    static int SelectedIndex(int[] values, int current)
    {
        for (int i = 0; i < values.Length; i++) if (values[i] == current) return i;
        return 1;
    }

    static int ThemeModeIndex(ThemeMode m) => m switch
    {
        ThemeMode.Light => 0,
        ThemeMode.Dark => 1,
        _ => 2,
    };

    // ================= 矢量图标(24×24 线形) =================

    static Canvas DrawDeviceIcon(DeviceKind kind, Color color)
    {
        var c = new Canvas { Width = 24, Height = 24 };
        var stroke = new SolidColorBrush(color);

        if (kind == DeviceKind.Mouse)
        {
            // 鼠标:圆角机身(胶囊)+ 顶部滚轮/按键分割线(不接机身边缘,避免看成袋子)
            c.Children.Add(RoundRect(5.5, 2, 13, 20, 6.5, null, stroke, 1.6));
            c.Children.Add(Line(12, 4.2, 12, 9.6, stroke, 1.6));
        }
        else if (kind == DeviceKind.Keyboard)
        {
            c.Children.Add(RoundRect(2.5, 7, 19, 12, 2, null, stroke, 1.6));
            c.Children.Add(Line(5.5, 10, 18.5, 10, stroke, 1.4));
            c.Children.Add(Line(5.5, 13, 18.5, 13, stroke, 1.4));
            c.Children.Add(Line(5.5, 16, 14.5, 16, stroke, 1.4));
            c.Children.Add(Line(17, 16, 19.5, 16, stroke, 1.4));
        }
        else // headset
        {
            var band = new Path
            {
                Data = Geometry.Parse("M4,14 L4,12 A8,8 0 0 1 20,12 L20,14"),
                Stroke = stroke,
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            c.Children.Add(band);
            c.Children.Add(RoundRect(3.5, 13, 4.5, 7, 2.25, null, stroke, 1.6));
            c.Children.Add(RoundRect(16, 13, 4.5, 7, 2.25, null, stroke, 1.6));
        }

        return c;
    }

    static Canvas DrawBack(Color color)
    {
        var c = new Canvas { Width = 24, Height = 24 };
        var stroke = new SolidColorBrush(color);
        var arrow = new Path
        {
            Data = Geometry.Parse("M14.5,6 L8.5,12 L14.5,18"),
            Stroke = stroke,
            StrokeThickness = 1.9,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        c.Children.Add(arrow);
        return c;
    }

    static Canvas DrawGear(Color color)
    {
        var c = new Canvas { Width = 24, Height = 24 };
        var stroke = new SolidColorBrush(color);
        const double cx = 12, cy = 12;

        // 8 个齿(径向短线,先画在齿轮环下面)
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            double cos = Math.Cos(a), sin = Math.Sin(a);
            c.Children.Add(Line(
                cx + cos * 6.5, cy + sin * 6.5,
                cx + cos * 9.7, cy + sin * 9.7,
                stroke, 2.0));
        }

        // 齿轮主体(环)
        var ring = new Ellipse { Width = 13, Height = 13, Stroke = stroke, StrokeThickness = 1.9 };
        Canvas.SetLeft(ring, cx - 6.5);
        Canvas.SetTop(ring, cy - 6.5);
        c.Children.Add(ring);

        // 中心孔
        var hole = new Ellipse { Width = 4.6, Height = 4.6, Stroke = stroke, StrokeThickness = 1.9 };
        Canvas.SetLeft(hole, cx - 2.3);
        Canvas.SetTop(hole, cy - 2.3);
        c.Children.Add(hole);

        return c;
    }

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

    // ================= 内嵌视图模型 =================

    sealed class DeviceRowView
    {
        public FrameworkElement Root = null!;
        public Border Icon = null!;
        public TextBlock Name = null!;
        public TextBlock Meta = null!;
        public Bar? Bar;
        public TextBlock? Pct;
        public readonly List<EarRow> Ears = new();
    }

    sealed class EarRow
    {
        public FrameworkElement Root = null!;
        public Bar Bar = null!;
        public TextBlock Pct = null!;
    }

    /// <summary>圆角进度条:用 Width 动画(ScaleX 会把填充圆角压成尖角)。</summary>
    sealed class Bar
    {
        public Grid Root;
        public Border Fill = null!;
        int level;

        public Bar()
        {
            Root = new Grid { Height = 7 };
            var track = new Border { CornerRadius = new CornerRadius(3.5), Background = new SolidColorBrush(Theme.BarTrack) };
            Root.Children.Add(track);
            Fill = new Border
            {
                CornerRadius = new CornerRadius(3.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
            };
            Root.Children.Add(Fill);
            Root.SizeChanged += (_, _) => ApplyWidth(false);
        }

        public void Set(int level)
        {
            this.level = level;
            Fill.Background = Theme.LevelBarBrush(level);
            ApplyWidth(AnimationsEnabled);
        }

        void ApplyWidth(bool animate)
        {
            if (Root.ActualWidth <= 0) return;
            double target = Math.Max(0, Math.Min(100, level)) / 100.0 * Root.ActualWidth;
            Fill.BeginAnimation(FrameworkElement.WidthProperty, null);
            if (!animate) { Fill.Width = target; return; }
            var anim = new DoubleAnimation(Fill.Width, target, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Fill.BeginAnimation(FrameworkElement.WidthProperty, anim);
        }
    }

    /// <summary>iOS 式开关。</summary>
    sealed class GlassToggle
    {
        public Border Root;
        readonly Border knob;
        readonly TranslateTransform knobShift;
        bool isOn;
        readonly Action<bool> onChanged;

        public GlassToggle(bool initial, Action<bool> onChanged)
        {
            this.onChanged = onChanged;
            isOn = initial;

            Root = new Border
            {
                Width = 40,
                Height = 23,
                CornerRadius = new CornerRadius(11.5),
                Cursor = Cursors.Hand,
                Background = new SolidColorBrush(initial ? Theme.ToggleOn : Theme.ToggleOff),
            };
            knob = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2.5, 0, 0, 0),
            };
            knob.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.35 };
            knobShift = new TranslateTransform(initial ? 17 : 0, 0);
            knob.RenderTransform = knobShift;
            Root.Child = knob;

            Root.MouseLeftButtonUp += (_, _) => Toggle();
        }

        void Toggle()
        {
            isOn = !isOn;
            Root.Background = new SolidColorBrush(isOn ? Theme.ToggleOn : Theme.ToggleOff);
            double to = isOn ? 17 : 0;
            if (AnimationsEnabled)
            {
                var anim = new DoubleAnimation(knobShift.X, to, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                knobShift.BeginAnimation(TranslateTransform.XProperty, anim);
            }
            else knobShift.X = to;
            onChanged(isOn);
        }
    }

    /// <summary>分段选择。</summary>
    sealed class SegmentedControl
    {
        public Border Root;
        readonly List<Border> buttons = new();
        readonly Action<int> onChanged;

        public SegmentedControl(string[] options, int selected, Action<int> onChanged)
        {
            this.onChanged = onChanged;

            Root = new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Theme.SegBg), Padding = new Thickness(3) };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 6, 12, 6),
                    Cursor = Cursors.Hand,
                };
                var t = new TextBlock
                {
                    Text = options[i],
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
                };
                b.Child = t;
                b.MouseLeftButtonUp += (_, _) => Select(idx);
                buttons.Add(b);
                panel.Children.Add(b);
            }
            Root.Child = panel;
            ApplyVisual(selected);
        }

        void Select(int idx)
        {
            ApplyVisual(idx);
            onChanged(idx);
        }

        void ApplyVisual(int idx)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                bool on = i == idx;
                buttons[i].Background = new SolidColorBrush(on ? Theme.SegOnBg : Colors.Transparent);
                if (on) buttons[i].Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.16 };
                else buttons[i].Effect = null;
                var t = (TextBlock)buttons[i].Child;
                t.Foreground = new SolidColorBrush(on ? Theme.SegOnFg : Theme.Muted);
                t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
    }

    /// <summary>−/＋ 步进器。</summary>
    sealed class Stepper
    {
        public Border Root;
        readonly TextBlock valueText;
        int value, min, max;
        readonly Action<int> onChanged;

        public Stepper(int initial, int min, int max, Action<int> onChanged)
        {
            value = initial;
            this.min = min;
            this.max = max;
            this.onChanged = onChanged;

            Root = new Border { CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Theme.SegBg), Padding = new Thickness(3) };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(StepButton("−", () => Set(value - 1)));
            valueText = new TextBlock
            {
                Width = 44,
                TextAlignment = TextAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Theme.Fg),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
            };
            panel.Children.Add(valueText);
            panel.Children.Add(StepButton("＋", () => Set(value + 1)));
            Root.Child = panel;
            Refresh();
        }

        Border StepButton(string glyph, Action click)
        {
            var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand };
            var t = new TextBlock
            {
                Text = glyph,
                FontSize = 15,
                Foreground = new SolidColorBrush(Theme.Muted),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            b.Child = t;
            b.MouseLeftButtonUp += (_, _) => click();
            return b;
        }

        void Set(int v)
        {
            value = Math.Clamp(v, min, max);
            Refresh();
            onChanged(value);
        }

        void Refresh() => valueText.Text = value + "%";
    }
}
