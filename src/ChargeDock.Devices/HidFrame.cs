using System;

namespace ChargeDock.Devices;

/// <summary>
/// 鼠标 HECATE G3M Pro 2.4G 接收器的 HID 报文纯字节逻辑(不碰 Win32,可单测)。
///
/// 逆向结论(见 BatteryWatchDemo/docs/README.md):
///   接收器 VID_35BB:PID_D3FB,HID 集合 MI_01&amp;Col05,厂商页 0xFF02,report ID 0x08,每帧 17 字节。
///   设备不主动广播,必须由主机查询。查询帧校验 = (0x55 − 前 16 字节之和) &amp; 0xFF。
/// </summary>
public static class HidFrame
{
    public const int FrameLength = 17;

    /// <summary>
    /// 生成一条"读电量"查询报文(17 字节):
    ///   08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 [校验]
    /// </summary>
    public static byte[] MakeBatteryQuery()
    {
        byte[] f = new byte[FrameLength];
        f[0] = 0x08;    // report ID
        f[1] = 0x04;    // 命令:读状态/电量
        int sum = 0;
        for (int i = 0; i < 16; i++) sum += f[i];
        f[16] = (byte)((0x55 - (sum & 0xFF)) & 0xFF);
        return f;
    }

    /// <summary>
    /// 解析回复报文: 08 04 .. .. .. 02 [电量] [充电] ...。
    /// 解析条件: buf[0]==0x08 &amp;&amp; buf[1]==0x04 &amp;&amp; buf[5]==0x02 → buf[6] 为电量。
    /// </summary>
    public static bool TryParseBattery(byte[] buf, int length, out int battery, out bool charging)
    {
        battery = -1;
        charging = false;
        if (length >= 8 && buf[0] == 0x08 && buf[1] == 0x04 && buf[5] == 0x02)
        {
            battery = buf[6];
            charging = buf[7] != 0;
            return true;
        }
        return false;
    }
}
