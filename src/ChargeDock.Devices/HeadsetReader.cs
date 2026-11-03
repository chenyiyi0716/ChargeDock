using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ChargeDock.Core;

using Gatt = Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace ChargeDock.Devices;

/// <summary>
/// 耳机 REDMI Buds 8 Pro 读取器:标准蓝牙 GATT 电池服务(0x180F / 0x2A19)。
/// 逆向成果从 BatteryWatch 的 Ble.cs 原样迁移,逻辑不变,改为 .NET 8 CsWinRT 投影。
///
/// 左右耳各一个 BLE 地址,两条线程分别读取;充电仓(充电盒)尚无逆向通道,恒为 null。
/// </summary>
public sealed class HeadsetReader : IBatteryReader
{
    const ulong AddressRight = 0xB85384E2B13EUL;
    const ulong AddressLeft  = 0xB85384F0EF5FUL;

    static readonly Guid BatteryService = new Guid("0000180f-0000-1000-8000-00805f9b34fb");
    static readonly Guid BatteryLevelChar = new Guid("00002a19-0000-1000-8000-00805f9b34fb");

    // ---- 后台线程写、UI 线程读,用 volatile ----
    volatile int leftBattery = -1;
    volatile int rightBattery = -1;
    volatile bool leftPresent;
    volatile bool rightPresent;
    volatile bool usingNotify;
    volatile string status = "启动中";
    volatile string lastProbe = "-";
    long lastSeenTicks;
    int generation;
    int retryDelayMs = 3000;
    volatile bool reconnectRequested;
    bool started;
    readonly List<object> keepAlive = new List<object>();

    public DeviceKind Kind => DeviceKind.Headset;
    public string DisplayName => "REDMI Buds 8 Pro";

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
        var t2 = new Thread(LeftLoop) { IsBackground = true };
        t2.Start();
    }

    public void RequestRefresh()
    {
        reconnectRequested = true;
    }

    public BatteryReading Snapshot()
    {
        int l = leftBattery;
        int r = rightBattery;
        bool lp = leftPresent && l >= 0;
        bool rp = rightPresent && r >= 0;
        DateTime seen = LastSeen;
        bool fresh = seen != DateTime.MinValue && (DateTime.Now - seen).TotalSeconds <= 90;
        bool connected = (lp || rp) && fresh;

        string st;
        if (!connected) st = (lp || rp) ? "连接中断,显示缓存电量" : status;
        else if (lp && rp) st = "双耳已连接";
        else st = "单耳已连接";

        return new BatteryReading
        {
            Kind = Kind,
            DisplayName = DisplayName,
            Left = lp ? l : null,
            Right = rp ? r : null,
            Case = null,     // 充电仓尚未逆向,恒为 null → 界面不显示该行
            Connected = connected,
            LastSeen = seen,
            Status = st,
        };
    }

    void Loop()
    {
        while (true)
        {
            try { Session(); }
            catch (Exception ex) { status = ex.Message; }
            Thread.Sleep(retryDelayMs);
        }
    }

    void Session()
    {
        int gen = ++generation;
        usingNotify = false;
        status = "连接中";
        rightBattery = -1;

        var dev = Wait(Windows.Devices.Bluetooth.BluetoothLEDevice.FromBluetoothAddressAsync(AddressRight), 8000);
        if (dev == null) { status = "找不到已配对的耳机"; Thread.Sleep(5000); return; }

        lock (keepAlive) { keepAlive.Clear(); keepAlive.Add(dev); }

        var svcs = Wait(dev.GetGattServicesAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), 10000);
        if (svcs.Status != Gatt.GattCommunicationStatus.Success)
        {
            status = "耳机未连接(" + svcs.Status + ")";
            dev.Dispose();
            return;
        }

        var chars = new List<Gatt.GattCharacteristic>();
        var slots = new List<int>();
        foreach (var s in svcs.Services)
        {
            if (s.Uuid != BatteryService) continue;
            var cr = Wait(s.GetCharacteristicsAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), 8000);
            if (cr.Status != Gatt.GattCommunicationStatus.Success) continue;

            int slot = 1;   // 主地址 = 右耳,固定槽位
            foreach (var c in cr.Characteristics)
            {
                if (c.Uuid != BatteryLevelChar) continue;
                chars.Add(c);
                slots.Add(slot);
            }
        }

        if (chars.Count == 0)
        {
            status = "耳机未暴露电池服务";
            dev.Dispose();
            return;
        }

        lock (keepAlive) keepAlive.AddRange(chars);

        rightPresent = true;

        bool canNotify = dev.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected;

        if (!canNotify)
        {
            try
            {
                var probe = Wait(chars[0].ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), 8000);
                lastProbe = "uncached=" + probe.Status + " conn=" + dev.ConnectionStatus;
                if (probe.Status == Gatt.GattCommunicationStatus.Success)
                {
                    SetLevel(1, ReadLevel(probe.Value));
                    canNotify = dev.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected;
                }
            }
            catch (Exception ex)
            {
                lastProbe = "uncached-ex: " + ex.Message;
            }
        }

        if (!canNotify)
        {
            for (int i = 0; i < chars.Count; i++) TryRead(chars[i], slots[i], gen);
        }

        retryDelayMs = (canNotify || lastProbe.IndexOf("Unreachable") < 0) ? 3000 : 60000;

        int subscribed = 0;
        for (int i = 0; i < chars.Count; i++)
        {
            var c = chars[i];
            int slot = slots[i];
            bool ok = false;
            try
            {
                var props = c.CharacteristicProperties;
                if (canNotify && (props & Gatt.GattCharacteristicProperties.Notify) != 0)
                {
                    var st = Wait(c.WriteClientCharacteristicConfigurationDescriptorAsync(
                        Gatt.GattClientCharacteristicConfigurationDescriptorValue.Notify), 5000);
                    ok = st == Gatt.GattCommunicationStatus.Success;
                }
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                int slotCopy = slot;
                Windows.Foundation.TypedEventHandler<Gatt.GattCharacteristic, Gatt.GattValueChangedEventArgs> handler =
                    (sender, args) =>
                    {
                        if (gen != generation) return;
                        try { SetLevel(slotCopy, ReadLevel(args.CharacteristicValue)); }
                        catch { }
                    };
                c.ValueChanged += handler;
                subscribed++;
            }
            else
            {
                TryRead(c, slot, gen);
            }
        }

        usingNotify = subscribed > 0;

        for (int i = 0; i < chars.Count; i++) TryRead(chars[i], slots[i], gen);

        status = usingNotify ? "已连接 · 监听通知" : (canNotify ? "已连接 · 主动读取" : "经典蓝牙连接 · 显示缓存电量");

        while (true)
        {
            Thread.Sleep(2000);
            if (gen != generation) return;
            if (reconnectRequested) { reconnectRequested = false; return; }

            if (canNotify)
            {
                if (dev.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Disconnected)
                {
                    status = "已断开";
                    return;
                }
            }
            else if (dev.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected)
            {
                return;
            }

            bool stale = LastSeen == DateTime.MinValue || (DateTime.Now - LastSeen).TotalSeconds > 20;
            if (stale)
            {
                for (int i = 0; i < chars.Count; i++) TryRead(chars[i], slots[i], gen);
            }
        }
    }

    void LeftLoop()
    {
        while (true)
        {
            bool ok = false;
            Windows.Devices.Bluetooth.BluetoothLEDevice? dev = null;
            try
            {
                dev = Wait(Windows.Devices.Bluetooth.BluetoothLEDevice.FromBluetoothAddressAsync(AddressLeft), 8000);
                if (dev != null)
                {
                    var svcs = Wait(dev.GetGattServicesAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), 10000);
                    if (svcs.Status == Gatt.GattCommunicationStatus.Success)
                    {
                        Gatt.GattCharacteristic? target = null;
                        foreach (var s in svcs.Services)
                        {
                            if (s.Uuid != BatteryService) continue;
                            var cr = Wait(s.GetCharacteristicsAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), 8000);
                            if (cr.Status != Gatt.GattCommunicationStatus.Success) continue;
                            foreach (var c in cr.Characteristics)
                            {
                                if (c.Uuid != BatteryLevelChar) continue;
                                target = c;
                                break;
                            }
                            if (target != null) break;
                        }

                        if (target != null)
                        {
                            var rr = Wait(target.ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), 8000);
                            if (rr.Status == Gatt.GattCommunicationStatus.Success)
                            {
                                leftBattery = ReadLevel(rr.Value);
                                ok = true;
                                Interlocked.Exchange(ref lastSeenTicks, DateTime.Now.Ticks);
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (dev != null) dev.Dispose();
            }

            if (!ok) leftBattery = -1;
            leftPresent = ok;

            Thread.Sleep(ok ? 20000 : 8000);
        }
    }

    static int ReadLevel(Windows.Storage.Streams.IBuffer buffer)
    {
        using var dr = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
        return dr.ReadByte();
    }

    void SetLevel(int slot, int level)
    {
        if (slot == 0) leftBattery = level;
        else rightBattery = level;
        Interlocked.Exchange(ref lastSeenTicks, DateTime.Now.Ticks);
    }

    void TryRead(Gatt.GattCharacteristic c, int slot, int gen)
    {
        try
        {
            var r = Wait(c.ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached), 8000);
            if (r.Status != Gatt.GattCommunicationStatus.Success) return;
            if (gen != generation) return;
            SetLevel(slot, ReadLevel(r.Value));
        }
        catch
        {
        }
    }

    /// <summary>阻塞等待一个 WinRT 异步操作完成(后台线程调用,阻塞无副作用)。</summary>
    static T Wait<T>(Windows.Foundation.IAsyncOperation<T> op, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (op.Status == Windows.Foundation.AsyncStatus.Started)
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("蓝牙操作超时");
            Thread.Sleep(15);
        }
        if (op.Status == Windows.Foundation.AsyncStatus.Completed) return op.GetResults();
        throw new Exception("蓝牙操作失败: " + op.Status);
    }
}
