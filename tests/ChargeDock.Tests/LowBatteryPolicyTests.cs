using ChargeDock.Core;
using Xunit;

namespace ChargeDock.Tests;

public class LowBatteryPolicyTests
{
    [Theory]
    [InlineData(DeviceKind.Mouse, true)]
    [InlineData(DeviceKind.Keyboard, true)]
    [InlineData(DeviceKind.Headset, false)]
    public void IsLowBatteryCandidate_OnlyMouseAndKeyboard(DeviceKind kind, bool expected)
    {
        Assert.Equal(expected, LowBatteryPolicy.IsLowBatteryCandidate(kind));
    }

    [Theory]
    [InlineData(15, 15, true)]
    [InlineData(14, 15, true)]
    [InlineData(0, 15, true)]
    [InlineData(16, 15, false)]
    [InlineData(100, 15, false)]
    public void IsLow_ThresholdBoundary(int level, int threshold, bool expected)
    {
        Assert.Equal(expected, LowBatteryPolicy.IsLow(level, threshold));
    }

    [Theory]
    [InlineData(21, 15, true)]
    [InlineData(20, 15, false)]
    [InlineData(16, 15, false)]
    [InlineData(15, 15, false)]
    public void ShouldReset_AboveThresholdPlusFive(int level, int threshold, bool expected)
    {
        Assert.Equal(expected, LowBatteryPolicy.ShouldReset(level, threshold));
    }

    [Theory]
    [InlineData(16, 15, null, false)]   // 高于阈值 → 不提醒
    [InlineData(15, 15, null, true)]    // 首次进入低电量 → 提醒
    [InlineData(10, 15, null, true)]    // 首次且很低 → 提醒
    [InlineData(15, 15, 15, false)]     // 与上次相同 → 不重复
    [InlineData(15, 15, 19, false)]     // 只跌了 4% → 不重复
    [InlineData(15, 15, 20, true)]      // 跌满 5% → 再提醒
    [InlineData(12, 15, 18, true)]      // 跌满 5% → 再提醒
    public void ShouldAlert_DedupLogic(int level, int threshold, int? lastNotified, bool expected)
    {
        Assert.Equal(expected, LowBatteryPolicy.ShouldAlert(level, threshold, lastNotified));
    }

    [Fact]
    public void DefaultThreshold_IsFifteen()
    {
        Assert.Equal(15, LowBatteryPolicy.DefaultThreshold);
    }
}
