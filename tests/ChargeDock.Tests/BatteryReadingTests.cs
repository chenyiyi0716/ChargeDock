using ChargeDock.Core;
using Xunit;

namespace ChargeDock.Tests;

public class BatteryReadingTests
{
    [Fact]
    public void HasAnyBattery_TrueWhenAnyValuePresent()
    {
        Assert.True(new BatteryReading { Kind = DeviceKind.Mouse, Battery = 50 }.HasAnyBattery);
        Assert.True(new BatteryReading { Kind = DeviceKind.Headset, Left = 50 }.HasAnyBattery);
        Assert.True(new BatteryReading { Kind = DeviceKind.Headset, Right = 50 }.HasAnyBattery);
        Assert.True(new BatteryReading { Kind = DeviceKind.Headset, Case = 50 }.HasAnyBattery);
    }

    [Fact]
    public void HasAnyBattery_FalseWhenAllNull()
    {
        Assert.False(new BatteryReading { Kind = DeviceKind.Mouse }.HasAnyBattery);
        Assert.False(new BatteryReading { Kind = DeviceKind.Headset }.HasAnyBattery);
        Assert.False(new BatteryReading { Kind = DeviceKind.Keyboard }.HasAnyBattery);
    }

    [Fact]
    public void ToString_ContainsKindAndName()
    {
        var r = new BatteryReading { Kind = DeviceKind.Mouse, DisplayName = "HECATE G3M PRO", Battery = 42 };
        Assert.Contains("HECATE G3M PRO", r.ToString());
        Assert.Contains("42%", r.ToString());
    }
}
