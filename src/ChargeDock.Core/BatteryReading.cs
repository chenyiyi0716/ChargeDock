using System;

namespace ChargeDock.Core;

/// <summary>
/// 一台设备某一时刻的读数快照,是设备层对外唯一的"数据载体"。
/// 所有字段只读,由 reader 一次性构造。
/// </summary>
public sealed class BatteryReading
{
    public DeviceKind Kind { get; init; }

    /// <summary>界面展示的设备型号名。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>单值设备(鼠标/键盘)的电量,null 表示未读到。</summary>
    public int? Battery { get; init; }

    /// <summary>耳机左耳电量,null 表示未读到/不在场。</summary>
    public int? Left { get; init; }

    /// <summary>耳机右耳电量,null 表示未读到/不在场。</summary>
    public int? Right { get; init; }

    /// <summary>耳机充电仓电量,能读到才非空(当前未逆向,恒为 null)。</summary>
    public int? Case { get; init; }

    /// <summary>是否充电中(仅鼠标有该标志)。</summary>
    public bool Charging { get; init; }

    /// <summary>本次是否"已连接"(有可用读数)。由 reader 依据新鲜度判定。</summary>
    public bool Connected { get; init; }

    /// <summary>最后一次拿到数据的时刻。</summary>
    public DateTime LastSeen { get; init; }

    /// <summary>给人看的状态文案(连接中/经典蓝牙缓存/等待唤醒等)。</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>是否存在至少一个可用数值(鼠标电量为空、耳机双耳+仓全空则为 false)。</summary>
    public bool HasAnyBattery =>
        Battery.HasValue || Left.HasValue || Right.HasValue || Case.HasValue;

    /// <summary>用于测试与诊断的字符串表示。</summary>
    public override string ToString()
    {
        string v = Kind switch
        {
            DeviceKind.Mouse or DeviceKind.Keyboard => Battery.HasValue ? $"{Battery}%" : "—",
            DeviceKind.Headset => $"L={(Left.HasValue ? Left + "%" : "—")} R={(Right.HasValue ? Right + "%" : "—")} C={(Case.HasValue ? Case + "%" : "—")}",
            _ => "?",
        };
        return $"{DisplayName}({Kind}): {v} connected={Connected}";
    }
}
