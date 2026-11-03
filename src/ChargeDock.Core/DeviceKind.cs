namespace ChargeDock.Core;

/// <summary>
/// 设备类型。数值与显示顺序无耦合,顺序由 <see cref="DeviceDisplayPolicy"/> 决定。
/// </summary>
public enum DeviceKind
{
    /// <summary>鼠标 HECATE G3M Pro(2.4G 接收器私有 HID)。</summary>
    Mouse,

    /// <summary>键盘 K820(固件不上报电量,当前无读数)。</summary>
    Keyboard,

    /// <summary>耳机 REDMI Buds 8 Pro(BLE GATT 0x180F / 0x2A19)。</summary>
    Headset,
}
