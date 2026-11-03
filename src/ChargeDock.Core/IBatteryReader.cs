namespace ChargeDock.Core;

/// <summary>
/// 设备读取器统一接口 —— 技术方案文档的核心边界:
/// UI 层只依赖此接口,不接触任何 Windows 设备 API;鼠标/耳机/键盘各一个实现。
/// </summary>
public interface IBatteryReader
{
    /// <summary>设备类型。</summary>
    DeviceKind Kind { get; }

    /// <summary>界面展示的设备型号名。</summary>
    string DisplayName { get; }

    /// <summary>启动后台监听线程。整个进程只调用一次,可重入。</summary>
    void Start();

    /// <summary>返回当前最新读数(纯读内存,不做任何 IO,可在 UI 线程调用)。</summary>
    BatteryReading Snapshot();

    /// <summary>请求立即重新建立连接/刷新(托盘菜单"立即刷新"用)。</summary>
    void RequestRefresh();
}
