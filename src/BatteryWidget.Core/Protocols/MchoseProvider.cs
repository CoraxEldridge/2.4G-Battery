using BatteryWidget.Core.Devices;
using BatteryWidget.Core.Hid;

namespace BatteryWidget.Core.Protocols;

/// <summary>
/// 迈从（MCHOSE）2.4G 键盘。电量走 0xFF04 使用页上的 8 字节 Feature Report（RID 0x06），
/// 回读格式：06 00 02 64 64 64 65 5A，其中第 3 字节为 0-100 的电量百分比。
/// </summary>
public sealed class MchoseProvider : IDeviceProvider
{
    public string Name => "迈从 0xFF04 电量";

    public const ushort VendorId = 0x41E4;
    private const byte BatteryReportId = 0x06;

    public static string IdFor(ushort productId) => $"mchose:{productId:X4}";

    public IReadOnlyList<DeviceSnapshot> Read(DeviceReadRequest request)
    {
        var list = new List<DeviceSnapshot>();
        var interfaces = HidEnumerator.Enumerate(includeDescriptor: false);

        foreach (var group in interfaces
                     .Where(i => i.VendorId == VendorId && i.UsagePage == 0xFF04 && i.FeatureReportLength > 0)
                     .GroupBy(i => i.ProductId))
        {
            var id = IdFor(group.Key);
            if (!request.Includes(id))
                continue;

            var device = group.First();
            var name = DeviceName(group.Key, device.Product);
            try
            {
                using var hid = HidDevice.Open(device, write: false);
                var report = hid.GetFeature(BatteryReportId, device.FeatureReportLength);
                if (report == null || report.Length < 4)
                {
                    list.Add(new DeviceSnapshot(id, name, DeviceKind.Keyboard, "2.4G", null, false,
                        Error: "读取电量 Feature Report 失败"));
                    continue;
                }

                var reading = Parse(report);
                list.Add(new DeviceSnapshot(id, name, DeviceKind.Keyboard, "2.4G", reading,
                    Online: reading != null,
                    Detail: $"Feature 0x{report[0]:X2}: {Hex(report)}"));
            }
            catch (Exception ex)
            {
                list.Add(new DeviceSnapshot(id, name, DeviceKind.Keyboard, "2.4G", null, false,
                    Error: ex.Message));
            }
        }

        return list;
    }

    /// <summary>解析 8 字节电池报告。</summary>
    public static BatteryReading? Parse(byte[] report)
    {
        if (report.Length < 8)
            return null;

        var percent = report[3];
        if (percent > 100)
            return null;

        // 实测：插拔充电线时 report[2] 不变（满电时恒为 0x02），
        // 因此 0x02 只能解释为"已充满"，不能当作"充电中"。
        var state = report[2];
        var status = state switch
        {
            0 => BatteryStatus.Discharging,
            1 => BatteryStatus.Charging,
            2 => BatteryStatus.Full,
            _ => BatteryStatus.Unknown
        };

        return new BatteryReading(percent, status, null, "迈从 0xFF04 / RID 0x06",
            $"state=0x{state:X2} 原始: {Hex(report)}");
    }

    public static string DeviceName(ushort productId, string productString)
    {
        if (productId == 0x2001)
            return "迈从 G87";
        return string.IsNullOrWhiteSpace(productString) ? $"迈从设备 {productId:X4}" : productString;
    }

    private static string Hex(byte[] data) => string.Join(' ', data.Select(b => b.ToString("X2")));
}
