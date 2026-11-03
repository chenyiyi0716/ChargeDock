// ============================================================================
//  ChargeDock 安装程序(单文件安装包)
//  1. 释放自包含主程序到 %LOCALAPPDATA%\Programs\ChargeDock\
//  2. 创建桌面快捷方式 + 开始菜单快捷方式
//  3. 写入卸载信息(设置 → 应用 里能看到 ChargeDock)
//  4. 启动主程序
//  参数:/silent 静默安装  /uninstall 卸载
// ============================================================================
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ChargeDock.Installer;

static class Paths
{
    public static readonly string InstallDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     @"Programs\ChargeDock");

    public static readonly string ExePath = Path.Combine(InstallDir, "ChargeDock.exe");

    public static readonly string DesktopLnk =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ChargeDock.lnk");

    public static readonly string StartMenuLnk =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "ChargeDock.lnk");

    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ChargeDock";
    public const string AppName = "ChargeDock";
    public const string Version = "1.0.0";
    public const string Publisher = "ChargeDock";
}

static class Installer
{
    public static void ExtractApp()
    {
        Directory.CreateDirectory(Paths.InstallDir);
        KillRunning();

        Assembly asm = Assembly.GetExecutingAssembly();
        using Stream? src = asm.GetManifestResourceStream("ChargeDock.exe");
        if (src == null) throw new Exception("安装包损坏:找不到内嵌的主程序");
        using FileStream dst = File.Create(Paths.ExePath);
        src.CopyTo(dst);
    }

    public static void KillRunning()
    {
        foreach (Process p in Process.GetProcessesByName("ChargeDock"))
        {
            try { p.Kill(); p.WaitForExit(3000); } catch { }
        }
        Thread.Sleep(300);
    }

    public static void CreateShortcut(string lnkPath, string target, string workDir, string desc)
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType!)!;

        object shortcut = shellType!.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
            null, shell, new object[] { lnkPath })!;

        Type sc = shortcut.GetType();
        sc.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
        sc.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workDir });
        sc.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { desc });
        sc.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { target + ",0" });
        sc.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
    }

    public static void RegisterUninstall(string setupExePath)
    {
        using RegistryKey? k = Registry.CurrentUser.CreateSubKey(Paths.UninstallKey);
        if (k == null) return;
        k.SetValue("DisplayName", Paths.AppName);
        k.SetValue("DisplayVersion", Paths.Version);
        k.SetValue("Publisher", Paths.Publisher);
        k.SetValue("InstallLocation", Paths.InstallDir);
        k.SetValue("DisplayIcon", Paths.ExePath);
        k.SetValue("UninstallString", "\"" + setupExePath + "\" /uninstall");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        k.SetValue("EstimatedSize", 188000, RegistryValueKind.DWord);
    }

    public static void Install(bool desktop, string setupExePath)
    {
        ExtractApp();
        CreateShortcut(Paths.StartMenuLnk, Paths.ExePath, Paths.InstallDir, Paths.AppName);
        if (desktop) CreateShortcut(Paths.DesktopLnk, Paths.ExePath, Paths.InstallDir, Paths.AppName);
        RegisterUninstall(setupExePath);
    }

    public static void StartApp()
    {
        try
        {
            var psi = new ProcessStartInfo(Paths.ExePath) { WorkingDirectory = Paths.InstallDir, UseShellExecute = false };
            Process.Start(psi);
        }
        catch { }
    }

    public static void Uninstall()
    {
        KillRunning();
        TryDelete(Paths.DesktopLnk);
        TryDelete(Paths.StartMenuLnk);
        try { Registry.CurrentUser.DeleteSubKeyTree(Paths.UninstallKey, false); } catch { }
        try { Directory.Delete(Paths.InstallDir, true); } catch { }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

class InstallForm : Form
{
    readonly CheckBox desktopBox;

    public InstallForm()
    {
        Text = Paths.AppName + " — 安装";
        ClientSize = new Size(430, 224);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x17, 0x17, 0x1C);
        ForeColor = Color.FromArgb(0xEC, 0xEC, 0xF1);
        Font = new Font("Microsoft YaHei UI", 9f);

        var title = new Label
        {
            Text = Paths.AppName,
            Font = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x3D, 0xDC, 0x97),
        };
        title.SetBounds(24, 20, 380, 34);
        Controls.Add(title);

        var sub = new Label
        {
            Text = "查看无线鼠标 / 键盘 / 耳机的实时电量 · 托盘常驻",
            ForeColor = Color.FromArgb(0x8A, 0x8A, 0x99),
        };
        sub.SetBounds(26, 56, 380, 20);
        Controls.Add(sub);

        var where = new Label
        {
            Text = @"安装位置:%LOCALAPPDATA%\Programs\ChargeDock",
            ForeColor = Color.FromArgb(0x5A, 0x5A, 0x66),
        };
        where.SetBounds(26, 88, 380, 20);
        Controls.Add(where);

        desktopBox = new CheckBox
        {
            Text = "创建桌面快捷方式",
            Checked = true,
            ForeColor = Color.FromArgb(0xEC, 0xEC, 0xF1),
        };
        desktopBox.SetBounds(26, 118, 380, 24);
        Controls.Add(desktopBox);

        var install = new Button
        {
            Text = "安装",
            BackColor = Color.FromArgb(0x3D, 0xDC, 0x97),
            ForeColor = Color.FromArgb(0x10, 0x20, 0x18),
            FlatStyle = FlatStyle.Flat,
        };
        install.FlatAppearance.BorderSize = 0;
        install.SetBounds(232, 160, 84, 34);
        install.Click += (_, _) => DoInstall();
        Controls.Add(install);

        var cancel = new Button
        {
            Text = "取消",
            BackColor = Color.FromArgb(0x2A, 0x2A, 0x33),
            ForeColor = Color.FromArgb(0xEC, 0xEC, 0xF1),
            FlatStyle = FlatStyle.Flat,
        };
        cancel.FlatAppearance.BorderSize = 0;
        cancel.SetBounds(324, 160, 84, 34);
        cancel.Click += (_, _) => Close();
        Controls.Add(cancel);

        AcceptButton = install;
        CancelButton = cancel;
    }

    void DoInstall()
    {
        try
        {
            Installer.Install(desktopBox.Checked, Environment.ProcessPath ?? Application.ExecutablePath);
            Installer.StartApp();
            MessageBox.Show("安装完成!\n\n程序已启动,图标在右下角托盘里。\n如果没看到,点任务栏的 ^ 展开。",
                Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("安装失败:\n" + ex.Message, Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool silent = Array.IndexOf(args, "/silent") >= 0;
        bool uninstall = Array.IndexOf(args, "/uninstall") >= 0;

        if (uninstall)
        {
            Installer.Uninstall();
            if (!silent)
                MessageBox.Show("已卸载。", Paths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (silent)
        {
            try
            {
                Installer.Install(true, Environment.ProcessPath ?? Application.ExecutablePath);
                Installer.StartApp();
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
            return;
        }

        Application.Run(new InstallForm());
    }

    static void LogError(Exception ex)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "setup-error.log"),
                ex.ToString());
        }
        catch { }
    }
}
