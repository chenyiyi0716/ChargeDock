using System;
using System.Runtime.InteropServices;
using System.Threading;
using ChargeDock.Core;

namespace ChargeDock.Devices;

/// <summary>
/// 鼠标 HECATE G3M Pro 读取器:2.4G 接收器私有 HID 协议,主动查询电量。
/// 逆向成果从 BatteryWatch 的 HecateHid.cs 原样迁移,逻辑不变。
/// </summary>
public sealed class MouseReader : IBatteryReader
{
    // ---- 要匹配的设备特征(换鼠标就改这三个常量) ----
    const ushort TargetVid = 0x35BB;
    const ushort TargetPid = 0xD3FB;
    const ushort TargetUsagePage = 0xFF02;   // 厂商自定义集合

    // HID 设备接口类 GUID,固定值
    static readonly Guid HidInterfaceClass = new Guid("4d1e55b2-f16f-11cf-88cb-001111000030");

    // ---- 运行时状态 ----
    string? currentPath;
    int debugCount;
    volatile int battery = -1;
    volatile bool charging;
    long lastSeenTicks;
    bool started;

    public DeviceKind Kind => DeviceKind.Mouse;
    public string DisplayName => "HECATE G3M Pro";

    public int Battery => battery;
    public bool Charging => charging;
    public string? CurrentPath => currentPath;

    /// <summary>最后一次成功收到电量报文的时刻。</summary>
    public DateTime LastSeen
    {
        get { long t = Interlocked.Read(ref lastSeenTicks); return t == 0 ? DateTime.MinValue : new DateTime(t); }
    }

    public void Start()
    {
        if (started) return;
        started = true;
        var t = new Thread(Loop) { IsBackground = true };
        t.Start();
    }

    public void RequestRefresh()
    {
        // 鼠标每 2 秒主动查询,无需特殊处理。
    }

    public BatteryReading Snapshot()
    {
        int b = battery;
        DateTime seen = LastSeen;
        bool fresh = b >= 0 && seen != DateTime.MinValue && (DateTime.Now - seen).TotalSeconds <= 3600;

        string status;
        if (b < 0) status = "等待鼠标唤醒…";
        else if (fresh) status = "已连接" + (charging ? " · 充电中" : "");
        else status = AgeText(seen) + (charging ? " · 充电中" : "");

        return new BatteryReading
        {
            Kind = Kind,
            DisplayName = DisplayName,
            Battery = b >= 0 ? b : null,
            Charging = charging,
            Connected = b >= 0 && fresh,
            LastSeen = seen,
            Status = status,
        };
    }

    static string AgeText(DateTime seen)
    {
        double age = (DateTime.Now - seen).TotalSeconds;
        if (age < 3600) return ((int)(age / 60)) + " 分钟前";
        return ((int)(age / 3600)) + " 小时前";
    }

    // ============================================================================
    //  Win32 HID P/Invoke 声明
    // ============================================================================
    [StructLayout(LayoutKind.Sequential)]
    struct Overlapped11
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr hEvent;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string n, uint a, uint s, IntPtr sec, uint d, uint f, IntPtr t);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(IntPtr h, byte[] b, int n, out int r, ref Overlapped11 ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool WriteFile(IntPtr h, byte[] b, int n, out int w, ref Overlapped11 ov);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetOverlappedResult(IntPtr h, ref Overlapped11 ov, out int t, bool w);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateEvent(IntPtr s, bool m, bool i, IntPtr n);
    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CancelIoEx(IntPtr h, IntPtr ov);

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize, ref int requiredSize, IntPtr devInfoData);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr hDevInfo);

    [DllImport("hid.dll")]
    static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES attributes);
    [DllImport("hid.dll")]
    static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr preparsedData);
    [DllImport("hid.dll")]
    static extern bool HidD_FreePreparsedData(IntPtr preparsedData);
    [DllImport("hid.dll")]
    static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS caps);

    /// <summary>打开一个设备路径,读出 VID/PID 和顶层 UsagePage。</summary>
    static bool ProbePath(string path, out ushort vid, out ushort pid, out ushort usagePage)
    {
        vid = 0; pid = 0; usagePage = 0;
        IntPtr h = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) return false;
        try
        {
            var attrs = new HIDD_ATTRIBUTES();
            attrs.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
            if (!HidD_GetAttributes(h, ref attrs)) return false;
            vid = attrs.VendorID;
            pid = attrs.ProductID;

            IntPtr pp;
            if (HidD_GetPreparsedData(h, out pp))
            {
                try
                {
                    var caps = new HIDP_CAPS();
                    if (HidP_GetCaps(pp, ref caps) >= 0) usagePage = caps.UsagePage;
                }
                finally
                {
                    HidD_FreePreparsedData(pp);
                }
            }
            return true;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>枚举所有 HID 接口,返回鼠标接收器那条厂商集合的路径。找不到返回 null。</summary>
    public string? Discover()
    {
        Guid guid = HidInterfaceClass;
        IntPtr hDevInfo = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, 0x02 | 0x10); // PRESENT | DEVICEINTERFACE
        if (hDevInfo == new IntPtr(-1)) return null;

        string? looseMatch = null;
        try
        {
            var did = new SP_DEVICE_INTERFACE_DATA();
            did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

            uint index = 0;
            while (SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref guid, index, ref did))
            {
                index++;

                int required = 0;
                SetupDiGetDeviceInterfaceDetailW(hDevInfo, ref did, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                if (required <= 0) continue;

                IntPtr buffer = Marshal.AllocHGlobal(required);
                try
                {
                    // cbSize 在 x64 上是 8、x86 上是 6
                    Marshal.WriteInt32(buffer, 0, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(hDevInfo, ref did, buffer, required, ref required, IntPtr.Zero))
                        continue;

                    // 结构是 { DWORD cbSize; WCHAR DevicePath[]; },字符串从偏移 4 开始
                    string path = Marshal.PtrToStringUni(new IntPtr(buffer.ToInt64() + 4))!;
                    if (string.IsNullOrEmpty(path)) continue;

                    ushort vid, pid, usagePage;
                    if (!ProbePath(path, out vid, out pid, out usagePage)) continue;
                    if (vid != TargetVid || pid != TargetPid) continue;

                    if (usagePage == TargetUsagePage) return path;   // 精确命中
                    if (looseMatch == null) looseMatch = path;       // 备选
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(hDevInfo);
        }

        return looseMatch;
    }

    static void HidLog(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hid.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
        }
        catch
        {
        }
    }

    /// <summary>常驻循环:枚举设备 → 打开 → 每 2 秒发一次查询、读一次回复。</summary>
    void Loop()
    {
        while (true)
        {
            if (currentPath == null) currentPath = Discover();
            if (currentPath == null) { Thread.Sleep(5000); continue; }

            IntPtr h = new IntPtr(-1);
            try
            {
                h = CreateFileW(currentPath, 0x80000000 | 0x40000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (h == new IntPtr(-1)) { currentPath = null; Thread.Sleep(3000); continue; }
                if (debugCount < 6) { debugCount++; HidLog("已打开设备: " + currentPath); }

                // 必须 auto-reset 事件
                IntPtr ev = CreateEvent(IntPtr.Zero, false, false, IntPtr.Zero);
                byte[] query = HidFrame.MakeBatteryQuery();
                byte[] buf = new byte[HidFrame.FrameLength];

                while (true)
                {
                    // ---- 1) 发查询 ----
                    Overlapped11 ovw = new Overlapped11();
                    ovw.hEvent = ev;
                    int wr = 0;
                    bool okw = WriteFile(h, query, HidFrame.FrameLength, out wr, ref ovw);
                    if (!okw)
                    {
                        int e = Marshal.GetLastWin32Error();
                        if (e != 997) { HidLog("写失败 e=" + e); break; }
                        if (WaitForSingleObject(ev, 1500) != 0) { CancelIoEx(h, IntPtr.Zero); HidLog("写超时"); break; }
                        if (!GetOverlappedResult(h, ref ovw, out wr, false)) { CancelIoEx(h, IntPtr.Zero); HidLog("写完成失败"); break; }
                    }

                    // ---- 2) 读回复 ----
                    Overlapped11 ov = new Overlapped11();
                    ov.hEvent = ev;
                    int rd = 0;
                    bool ok = ReadFile(h, buf, HidFrame.FrameLength, out rd, ref ov);
                    if (!ok)
                    {
                        int e = Marshal.GetLastWin32Error();
                        if (e != 997) break;
                        if (WaitForSingleObject(ev, 2000) != 0) { CancelIoEx(h, IntPtr.Zero); if (debugCount < 10) { debugCount++; HidLog("读超时"); } continue; }
                        if (!GetOverlappedResult(h, ref ov, out rd, false)) { CancelIoEx(h, IntPtr.Zero); continue; }
                    }

                    // ---- 3) 解析 ----
                    if (HidFrame.TryParseBattery(buf, rd, out int b, out bool chg))
                    {
                        battery = b;
                        charging = chg;
                        Interlocked.Exchange(ref lastSeenTicks, DateTime.Now.Ticks);
                    }
                    else if (battery < 0 && debugCount < 6)
                    {
                        debugCount++;
                        HidLog("循环: 写=" + okw + " 读长度=" + rd
                               + " 内容=" + BitConverter.ToString(buf, 0, Math.Max(0, Math.Min(rd, HidFrame.FrameLength)))
                               + " 查询帧=" + BitConverter.ToString(query));
                    }

                    Thread.Sleep(1500);
                }
            }
            catch
            {
                // 设备被拔掉时读写会抛异常,忽略,下面会重连
            }
            finally
            {
                if (h != new IntPtr(-1)) CloseHandle(h);
            }
            currentPath = null;
            Thread.Sleep(3000);
        }
    }
}
