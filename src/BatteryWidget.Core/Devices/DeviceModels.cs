namespace BatteryWidget.Core.Devices;

public enum BatteryStatus
{
    Unknown,
    Discharging,
    Charging,
    Full
}

public sealed record BatteryReading(
    int? Percent,
    BatteryStatus Status,
    int? VoltageMillivolts,
    string Source,
    string Detail = "")
{
    public bool HasPercent => Percent.HasValue;

    public string StatusText => Status switch
    {
        BatteryStatus.Charging => "充电中",
        BatteryStatus.Full => "已充满",
        BatteryStatus.Discharging => "放电中",
        _ => "状态未知"
    };
}

public enum DeviceKind
{
    Unknown,
    Keyboard,
    Mouse,
    Touchpad,
    Receiver
}

/// <summary>一次读取得到的设备快照。</summary>
public sealed record DeviceSnapshot(
    string Id,
    string Name,
    DeviceKind Kind,
    string Transport,
    BatteryReading? Battery,
    bool Online,
    string Detail = "",
    string? Error = null)
{
    public string KindText => Kind switch
    {
        DeviceKind.Keyboard => "无线键盘",
        DeviceKind.Mouse => "无线鼠标",
        DeviceKind.Touchpad => "无线触摸板",
        DeviceKind.Receiver => "接收器",
        _ => "无线设备"
    };
}

/// <summary>一次读取请求：是否深度唤醒、以及用户勾选的设备 ID。</summary>
public sealed record DeviceReadRequest(bool DeepWake = false, IReadOnlySet<string>? OnlyIds = null)
{
    /// <summary>未指定 ID 集合时表示读取全部。</summary>
    public bool Includes(string id) => OnlyIds == null || OnlyIds.Contains(id);
}

/// <summary>扫描到的可勾选设备。</summary>
public sealed record DeviceCandidate(
    string Id,
    string Name,
    DeviceKind Kind,
    string Transport,
    bool Supported,
    string Detail)
{
    public string KindText => Kind switch
    {
        DeviceKind.Keyboard => "键盘",
        DeviceKind.Mouse => "鼠标",
        DeviceKind.Touchpad => "触摸板",
        DeviceKind.Receiver => "接收器",
        _ => "设备"
    };
}

public interface IDeviceProvider
{
    string Name { get; }

    /// <summary>读取该 provider 负责的设备快照。不得抛异常。</summary>
    IReadOnlyList<DeviceSnapshot> Read(DeviceReadRequest request);
}
