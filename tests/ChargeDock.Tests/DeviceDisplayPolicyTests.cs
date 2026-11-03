using System.Collections.Generic;
using ChargeDock.Core;
using Xunit;

namespace ChargeDock.Tests;

public class DeviceDisplayPolicyTests
{
    static BatteryReading Mouse(int? battery, bool connected = true) => new BatteryReading
    {
        Kind = DeviceKind.Mouse,
        DisplayName = "HECATE G3M PRO",
        Battery = battery,
        Connected = connected,
    };

    static BatteryReading Headset(int? left, int? right, int? box = null, bool connected = true) => new BatteryReading
    {
        Kind = DeviceKind.Headset,
        DisplayName = "REDMI Buds 8 Pro",
        Left = left,
        Right = right,
        Case = box,
        Connected = connected,
    };

    [Fact]
    public void ShouldShowDevice_ConnectedWithData()
    {
        Assert.True(DeviceDisplayPolicy.ShouldShowDevice(Mouse(50)));
        Assert.True(DeviceDisplayPolicy.ShouldShowDevice(Headset(80, 90)));
        Assert.True(DeviceDisplayPolicy.ShouldShowDevice(Headset(80, null)));   // 单耳也算
    }

    [Fact]
    public void ShouldShowDevice_HidesUnconnectedOrEmpty()
    {
        Assert.False(DeviceDisplayPolicy.ShouldShowDevice(Mouse(null)));
        Assert.False(DeviceDisplayPolicy.ShouldShowDevice(Mouse(50, connected: false)));
        Assert.False(DeviceDisplayPolicy.ShouldShowDevice(Headset(null, null)));
    }

    [Fact]
    public void HeadsetSubRows_Independent()
    {
        var r = Headset(60, 70, 50);
        Assert.True(DeviceDisplayPolicy.ShouldShowLeftEar(r));
        Assert.True(DeviceDisplayPolicy.ShouldShowRightEar(r));
        Assert.True(DeviceDisplayPolicy.ShouldShowCase(r));

        var single = Headset(null, 70, null);
        Assert.False(DeviceDisplayPolicy.ShouldShowLeftEar(single));
        Assert.True(DeviceDisplayPolicy.ShouldShowRightEar(single));
        Assert.False(DeviceDisplayPolicy.ShouldShowCase(single));
    }

    [Fact]
    public void HeadsetSubRows_OnlyApplyToHeadset()
    {
        var m = Mouse(50);
        Assert.False(DeviceDisplayPolicy.ShouldShowLeftEar(m));
        Assert.False(DeviceDisplayPolicy.ShouldShowRightEar(m));
        Assert.False(DeviceDisplayPolicy.ShouldShowCase(m));
    }

    [Fact]
    public void OrderKey_MouseThenKeyboardThenHeadset()
    {
        Assert.True(DeviceDisplayPolicy.OrderKey(DeviceKind.Mouse) < DeviceDisplayPolicy.OrderKey(DeviceKind.Keyboard));
        Assert.True(DeviceDisplayPolicy.OrderKey(DeviceKind.Keyboard) < DeviceDisplayPolicy.OrderKey(DeviceKind.Headset));
    }

    [Fact]
    public void SelectVisible_FiltersAndOrders()
    {
        var readings = new List<BatteryReading>
        {
            Headset(80, 90),
            Mouse(50),
            Mouse(null),                 // 空读数 → 过滤
            new BatteryReading { Kind = DeviceKind.Keyboard, DisplayName = "K820", Connected = false }, // 键盘无读数 → 过滤
        };

        var visible = DeviceDisplayPolicy.SelectVisible(readings);
        Assert.Collection(visible,
            r => Assert.Equal(DeviceKind.Mouse, r.Kind),
            r => Assert.Equal(DeviceKind.Headset, r.Kind));
    }
}
