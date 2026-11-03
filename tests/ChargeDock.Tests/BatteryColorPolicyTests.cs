using ChargeDock.Core;
using Xunit;

namespace ChargeDock.Tests;

public class BatteryColorPolicyTests
{
    [Theory]
    [InlineData(100, BatteryLevelClass.High)]
    [InlineData(50, BatteryLevelClass.High)]
    [InlineData(49, BatteryLevelClass.Mid)]
    [InlineData(21, BatteryLevelClass.Mid)]
    [InlineData(20, BatteryLevelClass.Low)]
    [InlineData(0, BatteryLevelClass.Low)]
    public void Classify_MatchesDesignThresholds(int level, BatteryLevelClass expected)
    {
        Assert.Equal(expected, BatteryColorPolicy.Classify(level));
    }
}
