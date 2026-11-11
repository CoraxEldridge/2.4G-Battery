using BatteryWidget.Core.Devices;
using BatteryWidget.Core.Hid;

namespace BatteryWidget.Core.Protocols;

/// <summary>
/// 通用 HID 电量：读取设备在 Feature Report 里声明的标准电量字段
/// （HID 用法页 0x0006/0x0020 电池强度、0x0085 电池系统等）。
/// 只认显式声明的字段，避免瞎猜误报。
/// </summary>
public sealed class StandardBatteryProvider : IDeviceProvider
{
    public string Name => "标准 HID 电量字段";

    private static readonly (ushort Page, ushort Usage, string Name)[] BatteryUsages =
    {
        (0x0006, 0x0020, "Battery Strength（电池强度）"),
        (0x0085, 0x0066, "RelativeStateOfCharge（相对电量）"),
        (0x0085, 0x0065, "AbsoluteStateOfCharge（绝对电量）"),
        (0x000D, 0x003B, "触摸板电量字段")
    };

    public static string IdFor(HidInterface iface) => $"hid:{iface.VendorId:X4}:{iface.ProductId:X4}";

    public IReadOnlyList<DeviceSnapshot> Read(DeviceReadRequest request)
    {
        var list = new List<DeviceSnapshot>();
        var seen = new HashSet<string>();

        foreach (var iface in HidEnumerator.Enumerate(includeDescriptor: false))
        {
            if (iface.VendorId is 0x046D or 0x41E4)
                continue; // 这两个厂商走专用协议

            var id = IdFor(iface);
            if (!request.Includes(id) || !seen.Add(id))
                continue;

            var reading = TryRead(iface);
            if (reading == null)
                continue;

            list.Add(new DeviceSnapshot(
                id,
                string.IsNullOrWhiteSpace(iface.Product) ? $"HID 设备 {id}" : iface.Product,
                GuessKind(iface),
                "HID",
                reading,
                Online: true,
                Detail: $"usage=0x{iface.UsagePage:X4}/0x{iface.Usage:X4}"));
        }

        return list;
    }

    /// <summary>尝试从 Feature Report 的标准电量字段读一次。失败返回 null。</summary>
    public static BatteryReading? TryRead(HidInterface iface)
    {
        if (iface.FeatureReportLength <= 0)
            return null;

        HidDevice? device = null;
        try
        {
            device = HidDevice.Open(iface, write: false);
            if (device.PreparsedData == IntPtr.Zero)
                return null;

            var caps = HidValueCaps.GetFeatureCaps(device.Handle, device.PreparsedData);
            foreach (var cap in caps)
            {
                var match = BatteryUsages.FirstOrDefault(b => b.Page == cap.UsagePage && b.Usage == cap.Usage);
                if (match.Name == null)
                    continue;

                var report = device.GetFeature(cap.ReportId);
                if (report == null)
                    continue;

                var value = HidValueCaps.GetFeatureValue(device.Handle, device.PreparsedData, report,
                    cap.UsagePage, cap.Usage, report.Length);
                if (value == null)
                    continue;

                var percent = Scale(value.Value, cap.LogicalMax);
                if (percent == null)
                    continue;

                return new BatteryReading(percent, BatteryStatus.Unknown, null,
                    $"标准 HID 电量字段（{match.Name}）",
                    $"ReportID=0x{cap.ReportId:X2} 原始值={value} 上限={cap.LogicalMax}");
            }
        }
        catch
        {
            // 设备可能被占用或无权限，忽略
        }
        finally
        {
            device?.Dispose();
        }

        return null;
    }

    private static int? Scale(uint value, int logicalMax)
    {
        if (value > 100 && logicalMax <= 0)
            return null;

        var percent = value switch
        {
            <= 100 => (int)value,
            _ when logicalMax > 100 => (int)Math.Round(value * 100.0 / logicalMax),
            _ => (int)Math.Round(value * 100.0 / 255)
        };

        return percent is >= 0 and <= 100 ? percent : null;
    }

    private static DeviceKind GuessKind(HidInterface iface) => iface.UsagePage switch
    {
        0x0001 when iface.Usage == 0x0006 => DeviceKind.Keyboard,
        0x0001 when iface.Usage == 0x0002 => DeviceKind.Mouse,
        0x000D => DeviceKind.Touchpad,
        _ => DeviceKind.Unknown
    };
}
