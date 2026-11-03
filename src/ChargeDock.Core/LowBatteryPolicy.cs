namespace ChargeDock.Core;

/// <summary>
/// 低电量提醒策略 —— 纯逻辑,可脱离真机单测。
///
/// 产品规则(见产品设计文档 §6/§7):
///   · 只对鼠标、键盘做低电量提醒,耳机不做。
///   · 阈值 15%,≤ 15% 触发,方式为卡片变色 + 弹窗。
/// </summary>
public static class LowBatteryPolicy
{
    /// <summary>默认低电量阈值(百分比)。产品文档固定为 15%。</summary>
    public const int DefaultThreshold = 15;

    /// <summary>是否属于"需要做低电量提醒"的设备类型(仅鼠标、键盘)。</summary>
    public static bool IsLowBatteryCandidate(DeviceKind kind) =>
        kind == DeviceKind.Mouse || kind == DeviceKind.Keyboard;

    /// <summary>电平是否处于低电量区间。</summary>
    public static bool IsLow(int level, int threshold) => level <= threshold;

    /// <summary>
    /// 回血后是否应清除"已提醒"记录。高于阈值 + 5% 视为回血,
    /// 这样电量再次掉回低区间时可以再次提醒。
    /// </summary>
    public static bool ShouldReset(int level, int threshold) => level > threshold + 5;

    /// <summary>
    /// 是否应触发一次提醒。规则:
    ///   · 高于阈值 → 不提醒;
    ///   · 低于阈值但距上次提醒未跌满 5% → 不重复提醒(避免每 3 秒弹一次);
    ///   · 首次(无上次记录)或已跌满 5% → 提醒。
    /// </summary>
    public static bool ShouldAlert(int level, int threshold, int? lastNotifiedLevel)
    {
        if (level > threshold) return false;
        if (lastNotifiedLevel.HasValue && lastNotifiedLevel.Value - level < 5) return false;
        return true;
    }
}
