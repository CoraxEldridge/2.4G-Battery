namespace BatteryWidget.Core.Hid;

/// <summary>一条 HID 接口（一个 top-level collection）的静态信息。</summary>
public sealed class HidInterface
{
    public required string Path { get; init; }
    public ushort VendorId { get; init; }
    public ushort ProductId { get; init; }
    public ushort VersionNumber { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public int InputReportLength { get; init; }
    public int OutputReportLength { get; init; }
    public int FeatureReportLength { get; init; }
    public string Product { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Serial { get; init; } = "";
    public byte[]? ReportDescriptor { get; init; }
    public string InstanceSuffix { get; init; } = "";

    public string VidPid => $"{VendorId:X4}:{ProductId:X4}";

    public override string ToString() =>
        $"{VidPid} usage=0x{UsagePage:X4}/0x{Usage:X4} in={InputReportLength} out={OutputReportLength} feat={FeatureReportLength} {Path}";
}
