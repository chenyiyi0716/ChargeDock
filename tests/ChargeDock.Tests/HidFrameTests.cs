using ChargeDock.Devices;
using Xunit;

namespace ChargeDock.Tests;

public class HidFrameTests
{
    [Fact]
    public void MakeBatteryQuery_LengthIsSeventeen()
    {
        Assert.Equal(17, HidFrame.MakeBatteryQuery().Length);
    }

    [Fact]
    public void MakeBatteryQuery_ReportIdAndCommand()
    {
        var f = HidFrame.MakeBatteryQuery();
        Assert.Equal(0x08, f[0]);   // report ID
        Assert.Equal(0x04, f[1]);   // 命令:读状态/电量
    }

    [Fact]
    public void MakeBatteryQuery_Checksum_MatchesReverseEngineeredValue()
    {
        // 逆向结论:查询帧固定以 0x49 结尾(见 BatteryWatchDemo/docs/README.md)
        var f = HidFrame.MakeBatteryQuery();
        Assert.Equal(0x49, f[16]);
    }

    [Fact]
    public void MakeBatteryQuery_ChecksumFormula()
    {
        var f = HidFrame.MakeBatteryQuery();
        int sum = 0;
        for (int i = 0; i < 16; i++) sum += f[i];
        Assert.Equal((0x55 - (sum & 0xFF)) & 0xFF, f[16]);
    }

    [Fact]
    public void TryParseBattery_ValidFrame_ReadsLevelAndCharging()
    {
        byte[] buf = { 0x08, 0x04, 0x00, 0x00, 0x00, 0x02, 0x5F, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.True(HidFrame.TryParseBattery(buf, buf.Length, out int battery, out bool charging));
        Assert.Equal(0x5F, battery);   // 95%
        Assert.False(charging);
    }

    [Fact]
    public void TryParseBattery_ChargingFlag()
    {
        byte[] buf = { 0x08, 0x04, 0x00, 0x00, 0x00, 0x02, 0x32, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        Assert.True(HidFrame.TryParseBattery(buf, buf.Length, out int battery, out bool charging));
        Assert.Equal(0x32, battery);   // 50%
        Assert.True(charging);
    }

    [Theory]
    [InlineData(0x01)]   // 命令不是 0x04
    [InlineData(0x03)]   // 心跳
    public void TryParseBattery_WrongCommand_ReturnsFalse(byte cmd)
    {
        byte[] buf = new byte[17];
        buf[0] = 0x08;
        buf[1] = cmd;
        buf[5] = 0x02;
        Assert.False(HidFrame.TryParseBattery(buf, buf.Length, out _, out _));
    }

    [Fact]
    public void TryParseBattery_WrongPayloadMarker_ReturnsFalse()
    {
        byte[] buf = new byte[17];
        buf[0] = 0x08;
        buf[1] = 0x04;
        buf[5] = 0x00;   // 不是 0x02 → 不是状态包
        Assert.False(HidFrame.TryParseBattery(buf, buf.Length, out _, out _));
    }

    [Fact]
    public void TryParseBattery_TooShort_ReturnsFalse()
    {
        byte[] buf = { 0x08, 0x04, 0x00, 0x00, 0x00, 0x02 };
        Assert.False(HidFrame.TryParseBattery(buf, buf.Length, out _, out _));
    }
}
