using System;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace ChargeDock.App;

/// <summary>
/// 玻璃拟态的亚克力毛玻璃效果(Windows 10/11 DWM)。
/// 通过 SetWindowCompositionAttribute 施加 ACCENT_ENABLE_ACRYLICBLURBEHIND(真实背景模糊 + 着色),
/// 圆角用 CreateRoundRectRgn + SetWindowRgn 实现(非分层窗口无法用 AllowsTransparency 做圆角)。
/// </summary>
static class GlassEffect
{
    [DllImport("user32.dll")]
    static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]
    static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWCP_ROUND = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    const int WCA_ACCENT_POLICY = 19;
    const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    /// <summary>施加亚克力毛玻璃。tint 的 alpha 控制着色强度,A 越高越不透明。</summary>
    public static bool ApplyAcrylic(IntPtr hwnd, Color tint)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            // GradientColor 是 AABBGGRR
            int abgr = ((int)tint.A << 24) | ((int)tint.B << 16) | ((int)tint.G << 8) | (int)tint.R;
            var accent = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GradientColor = abgr,
            };
            int size = Marshal.SizeOf(typeof(AccentPolicy));
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
                return SetWindowCompositionAttribute(hwnd, ref data) == 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Windows 11 原生圆角:DWM 会把亚克力层也裁成圆角(SetWindowRgn 只裁 WPF 内容,裁不住 DWM 合成的亚克力)。</summary>
    public static bool ApplyNativeRoundedCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            int pref = DWMWCP_ROUND;
            return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>给窗口设圆角区域(区域句柄归系统所有,不要 DeleteObject)。</summary>
    public static bool ApplyRoundedRegion(IntPtr hwnd, int width, int height, int radius)
    {
        if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return false;
        try
        {
            IntPtr rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
            return SetWindowRgn(hwnd, rgn, true) != 0;
        }
        catch
        {
            return false;
        }
    }
}
