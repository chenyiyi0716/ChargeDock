using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ChargeDock.Core;
using ChargeDock.Devices;
using WinForms = System.Windows.Forms;
using Gdi = System.Drawing;

namespace ChargeDock.App;

public static class Program
{
    const string MutexName = "ChargeDock_SingleInstance";
    const string ShowEventName = "ChargeDock_ShowPanel";

    static readonly List<IBatteryReader> readers = new()
    {
        new MouseReader(),
        new KeyboardReader(),
        new HeadsetReader(),
    };

    static Flyout flyout = null!;
    static TrayMenu? trayMenu;
    static WinForms.NotifyIcon? tray;
    static DispatcherTimer? uiTimer;
    static SynchronizationContext? trayContext;
    static readonly ManualResetEvent trayReady = new(false);
    static int lastIconLevel = -2;
    static IntPtr currentIconHandle;
    static readonly Dictionary<DeviceKind, int> notified = new();

    [STAThread]
    public static void Main(string[] args)
    {
        WinForms.Application.SetHighDpiMode(WinForms.HighDpiMode.PerMonitorV2);

        // ---- 诊断模式:不开界面 ----
        if (args.Length >= 2 && args[0] == "--selftest")
        {
            RunSelfTest(args[1]);
            return;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log("UI 线程异常: " + e.Exception); e.Handled = true; };

        Theme.Mode = ParseThemeMode(Settings.ThemeMode);

        flyout = new Flyout();
        flyout.RefreshRequested += OnManualRefresh;
        flyout.SettingsChanged += OnSettingsChanged;

        // ---- 渲染模式:把卡片画成 PNG,检查布局 ----
        if (args.Length >= 2 && args[0] == "--render")
        {
            Flyout.AnimationsEnabled = false;
            flyout.Render(FakeReadings());
            flyout.ApplyRenderFallback();
            RenderToFile(args[1]);
            return;
        }

        // ---- 单实例 ----
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            return;
        }

        trayMenu = new TrayMenu();
        trayMenu.ShowCardRequested += () => flyout.Toggle();
        trayMenu.RefreshRequested += OnManualRefresh;
        trayMenu.SettingsRequested += () => { flyout.SetView(true); flyout.Show(); };
        trayMenu.AboutRequested += ShowAbout;
        trayMenu.ExitRequested += ExitApp;

        BuildTray();
        foreach (var r in readers) r.Start();

        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (true)
            {
                showEvent.WaitOne();
                flyout.UiDispatcher.BeginInvoke(new Action(flyout.Show));
            }
        }) { IsBackground = true };
        listener.Start();

        FirstRunTip();

        uiTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Settings.RefreshIntervalSeconds) };
        uiTimer.Tick += (_, _) => RefreshAll();
        uiTimer.Start();

        RefreshAll();
        app.Run();
    }

    static ThemeMode ParseThemeMode(string s) => s switch
    {
        "light" => ThemeMode.Light,
        "dark" => ThemeMode.Dark,
        _ => ThemeMode.System,
    };

    static void OnSettingsChanged()
    {
        if (uiTimer != null)
            uiTimer.Interval = TimeSpan.FromSeconds(Settings.RefreshIntervalSeconds);
        RefreshTrayIconNow();
    }

    static void ShowAbout()
    {
        MessageBox.Show("ChargeDock v0.1.0\n\n常驻桌面的个人外设电量卡片。\n鼠标 / 键盘 / 耳机左右耳电量一目了然。",
            "关于 ChargeDock", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    static void OnManualRefresh()
    {
        foreach (var r in readers) r.RequestRefresh();
        RefreshAll();
    }

    static void FirstRunTip()
    {
        if (Settings.TipShown) return;
        PostTray(() =>
        {
            try
            {
                tray?.ShowBalloonTip(6000, "ChargeDock 已启动",
                    "图标在右下角托盘里。如果没看到,点任务栏的 ^ 展开,把它拖到外面。",
                    WinForms.ToolTipIcon.Info);
            }
            catch { }
        });
        try { Settings.TipShown = true; } catch { }
        flyout.UiDispatcher.BeginInvoke(new Action(flyout.Show), DispatcherPriority.ApplicationIdle);
    }

    // ================= 数据刷新 =================

    static void RefreshAll()
    {
        var readings = readers.Select(r => r.Snapshot()).ToList();
        var visible = DeviceDisplayPolicy.SelectVisible(readings).ToList();

        flyout.Render(visible);

        UpdateTray(visible);
        CheckLowBattery(visible);
    }

    static int MinLevel(BatteryReading r)
    {
        int min = int.MaxValue;
        if (r.Battery.HasValue) min = Math.Min(min, r.Battery.Value);
        if (r.Left.HasValue) min = Math.Min(min, r.Left.Value);
        if (r.Right.HasValue) min = Math.Min(min, r.Right.Value);
        if (r.Case.HasValue) min = Math.Min(min, r.Case.Value);
        return min == int.MaxValue ? -1 : min;
    }

    static void CheckLowBattery(IReadOnlyList<BatteryReading> visible)
    {
        if (!Settings.LowBatteryEnabled || !Settings.PopupNotify) return;
        int threshold = Settings.LowBatteryThreshold;

        foreach (var r in visible)
        {
            if (!LowBatteryPolicy.IsLowBatteryCandidate(r.Kind)) continue;
            if (!r.Battery.HasValue) continue;
            int level = r.Battery.Value;

            if (LowBatteryPolicy.ShouldReset(level, threshold))
            {
                notified.Remove(r.Kind);
                continue;
            }

            int? last = notified.TryGetValue(r.Kind, out var l) ? l : null;
            if (!LowBatteryPolicy.ShouldAlert(level, threshold, last)) continue;

            notified[r.Kind] = level;
            string name = r.DisplayName;
            PostTray(() =>
            {
                try
                {
                    tray?.ShowBalloonTip(6000, "电量偏低", name + " 剩余 " + level + "%",
                        level <= 10 ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Warning);
                }
                catch { }
            });
        }
    }

    // ================= 托盘 =================

    static void BuildTray()
    {
        var t = new Thread(TrayThread) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        trayReady.WaitOne(5000);
    }

    static void PostTray(Action action)
    {
        var ctx = trayContext;
        if (ctx == null) { action(); return; }
        try { ctx.Post(_ => action(), null); } catch { }
    }

    static void TrayThread()
    {
        try
        {
            WinForms.Application.EnableVisualStyles();
            SynchronizationContext.SetSynchronizationContext(new WinForms.WindowsFormsSynchronizationContext());
            trayContext = SynchronizationContext.Current!;

            tray = new WinForms.NotifyIcon
            {
                Icon = MakeIcon(-1),
                Text = "ChargeDock",
                Visible = true,
            };

            tray.MouseClick += (_, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left)
                {
                    flyout.UiDispatcher.BeginInvoke(new Action(flyout.Toggle));
                }
                else if (e.Button == WinForms.MouseButtons.Right)
                {
                    var pos = WinForms.Cursor.Position;
                    int x = pos.X, y = pos.Y;
                    flyout.UiDispatcher.BeginInvoke(new Action(() => trayMenu?.ShowAt(x, y)));
                }
            };

            trayReady.Set();
            WinForms.Application.Run();
        }
        catch (Exception ex)
        {
            Log("托盘线程异常: " + ex);
            trayReady.Set();
        }
    }

    static Gdi.Icon MakeIcon(int level)
    {
        using var bmp = TrayIconFactory.Render(level);
        IntPtr h = bmp.GetHicon();
        currentIconHandle = h;
        return Gdi.Icon.FromHandle(h);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr handle);

    static string BuildTrayTip(IReadOnlyList<BatteryReading> visible)
    {
        var sb = new System.Text.StringBuilder("ChargeDock");
        foreach (var r in visible)
        {
            sb.Append(" · ");
            if (r.Kind == DeviceKind.Headset)
            {
                var parts = new List<string>();
                if (r.Left.HasValue) parts.Add("L" + r.Left + "%");
                if (r.Right.HasValue) parts.Add("R" + r.Right + "%");
                if (r.Case.HasValue) parts.Add("C" + r.Case + "%");
                sb.Append(parts.Count > 0 ? string.Join("/", parts) : r.DisplayName);
            }
            else
            {
                sb.Append(r.Battery + "%");
            }
        }
        string text = sb.ToString();
        return text.Length > 62 ? text.Substring(0, 62) : text;
    }

    static void UpdateTray(IReadOnlyList<BatteryReading> visible)
    {
        if (tray == null) return;

        int level = -1;
        foreach (var r in visible)
        {
            int l = MinLevel(r);
            if (l >= 0) level = level < 0 ? l : Math.Min(level, l);
        }

        trayMenu?.SetBattery(level);

        string text = BuildTrayTip(visible);

        PostTray(() =>
        {
            if (tray == null) return;
            if (Math.Abs(level - lastIconLevel) >= 2 || lastIconLevel == -2)
            {
                lastIconLevel = level;
                Gdi.Icon? oldIcon = tray.Icon;
                IntPtr oldHandle = currentIconHandle;
                tray.Icon = MakeIcon(level);
                oldIcon?.Dispose();
                if (oldHandle != IntPtr.Zero) DestroyIcon(oldHandle);
            }
            tray.Text = text;
        });
    }

    static void RefreshTrayIconNow()
    {
        lastIconLevel = -2;
        RefreshAll();
    }

    static void ExitApp()
    {
        uiTimer?.Stop();
        PostTray(() =>
        {
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            WinForms.Application.ExitThread();
        });
        flyout.UiDispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
    }

    // ================= 诊断 =================

    static void RunSelfTest(string path)
    {
        foreach (var r in readers) r.Start();
        Thread.Sleep(20000);
        var sb = new System.Text.StringBuilder();
        foreach (var r in readers)
        {
            var snap = r.Snapshot();
            sb.AppendLine(snap.ToString());
        }
        File.WriteAllText(path, sb.ToString());
    }

    static List<BatteryReading> FakeReadings() => new()
    {
        new BatteryReading { Kind = DeviceKind.Mouse, DisplayName = "HECATE G3M Pro", Battery = 82, Connected = true },
        new BatteryReading { Kind = DeviceKind.Keyboard, DisplayName = "K820", Battery = 12, Connected = true },
        new BatteryReading { Kind = DeviceKind.Headset, DisplayName = "REDMI Buds 8 Pro", Left = 45, Right = 70, Case = 100, Connected = true },
    };

    static void RenderToFile(string path)
    {
        var root = flyout.Card;
        root.Measure(new Size(320, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, 320, root.DesiredSize.Height));
        root.UpdateLayout();

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight),
            96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(root);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }

    static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
        }
        catch { }
    }
}
