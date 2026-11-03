using System;
using ChargeDock.Core;

namespace ChargeDock.Devices;

/// <summary>
/// 键盘 K820 读取器。
///
/// 按技术方案文档 §3.1:键盘固件不上报电量,当前"无解,先搁置"。
/// 因此该 reader 存在(接口各一个实现),但永远不产生读数,界面不会显示它;
/// 低电量策略仍把它列为候选(产品文档 §6 要求),只是永远不会触发。
/// </summary>
public sealed class KeyboardReader : IBatteryReader
{
    public DeviceKind Kind => DeviceKind.Keyboard;
    public string DisplayName => "K820";

    public void Start()
    {
        // 无数据通道,什么都不做。
    }

    public void RequestRefresh()
    {
        // 无数据通道,什么都不做。
    }

    public BatteryReading Snapshot() => new BatteryReading
    {
        Kind = Kind,
        DisplayName = DisplayName,
        Battery = null,
        Connected = false,
        LastSeen = DateTime.MinValue,
        Status = "固件不上报电量",
    };
}
