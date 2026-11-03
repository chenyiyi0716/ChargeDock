namespace ChargeDock.Core;

/// <summary>电量颜色分级(见 UI 设计文档 §4)。</summary>
public enum BatteryLevelClass
{
    /// <summary>≥ 50%,绿色。</summary>
    High,

    /// <summary>20% – 50%,黄色。</summary>
    Mid,

    /// <summary>≤ 20%,红色(低电量)。</summary>
    Low,
}

/// <summary>电量颜色分级规则 —— 纯逻辑,可单测。</summary>
public static class BatteryColorPolicy
{
    public const int HighThreshold = 50;   // ≥50 绿
    public const int LowThreshold = 20;    // ≤20 红

    /// <summary>按电量分级:≥50 高 / (20,50) 中 / ≤20 低。</summary>
    public static BatteryLevelClass Classify(int level) =>
        level >= HighThreshold ? BatteryLevelClass.High
        : level > LowThreshold ? BatteryLevelClass.Mid
        : BatteryLevelClass.Low;
}
