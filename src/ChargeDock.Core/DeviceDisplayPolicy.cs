using System.Collections.Generic;
using System.Linq;

namespace ChargeDock.Core;

/// <summary>
/// 设备显示规则 —— 纯逻辑,可单测。
///
/// 产品规则(见产品设计文档 §6/§7):
///   · 只显示已连接(有可用读数)的设备;
///   · 耳机左右耳独立判断,充电仓能读到才显示;
///   · 未连接的设备/单耳不在卡片中显示。
/// </summary>
public static class DeviceDisplayPolicy
{
    /// <summary>是否在卡片中显示该设备(已连接且有可用读数)。</summary>
    public static bool ShouldShowDevice(BatteryReading r) =>
        r.Connected && r.HasAnyBattery;

    /// <summary>是否显示耳机左耳行。</summary>
    public static bool ShouldShowLeftEar(BatteryReading r) =>
        r.Kind == DeviceKind.Headset && r.Left.HasValue;

    /// <summary>是否显示耳机右耳行。</summary>
    public static bool ShouldShowRightEar(BatteryReading r) =>
        r.Kind == DeviceKind.Headset && r.Right.HasValue;

    /// <summary>是否显示耳机充电仓行。</summary>
    public static bool ShouldShowCase(BatteryReading r) =>
        r.Kind == DeviceKind.Headset && r.Case.HasValue;

    /// <summary>固定展示顺序:鼠标 → 键盘 → 耳机。</summary>
    public static int OrderKey(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => 0,
        DeviceKind.Keyboard => 1,
        DeviceKind.Headset => 2,
        _ => 3,
    };

    /// <summary>过滤出"应显示"的设备并排好序。</summary>
    public static IEnumerable<BatteryReading> SelectVisible(
        IEnumerable<BatteryReading> readings) =>
        readings.Where(ShouldShowDevice).OrderBy(r => OrderKey(r.Kind));
}
