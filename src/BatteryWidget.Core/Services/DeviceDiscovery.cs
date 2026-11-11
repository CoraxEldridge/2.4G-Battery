using BatteryWidget.Core.Devices;
using BatteryWidget.Core.Hid;
using BatteryWidget.Core.Protocols;

namespace BatteryWidget.Core.Services;

/// <summary>扫描所有能读电量的 2.4G / HID 设备，供用户勾选。</summary>
public sealed class DeviceDiscovery
{
    public IReadOnlyList<DeviceCandidate> Scan(Action<string>? progress = null)
    {
        var candidates = new List<DeviceCandidate>();
        var seen = new HashSet<string>();
        var interfaces = HidEnumerator.Enumerate(includeDescriptor: false);

        // ---- 迈从 ----
        progress?.Invoke("扫描迈从设备…");
        foreach (var group in interfaces
                     .Where(i => i.VendorId == MchoseProvider.VendorId && i.UsagePage == 0xFF04 && i.FeatureReportLength > 0)
                     .GroupBy(i => i.ProductId))
        {
            var id = MchoseProvider.IdFor(group.Key);
            if (!seen.Add(id))
                continue;

            var first = group.First();
            var reading = ProbeMchose(first);
            candidates.Add(new DeviceCandidate(
                id,
                MchoseProvider.DeviceName(group.Key, first.Product),
                DeviceKind.Keyboard,
                "2.4G",
                reading != null,
                reading != null
                    ? "电量通道 0xFF04 / RID 0x06"
                    : "找到设备，但电量通道无响应"));
        }

        // ---- 罗技 ----
        progress?.Invoke("扫描罗技接收器…");
        foreach (var snapshot in LogitechProvider.ScanPairedDevices(interfaces))
        {
            if (!seen.Add(snapshot.Id))
                continue;

            candidates.Add(new DeviceCandidate(
                snapshot.Id,
                snapshot.Name,
                snapshot.Kind,
                "2.4G",
                snapshot.Battery?.Percent != null,
                snapshot.Detail));
        }

        // ---- 其它声明了标准电量字段的 HID 设备 ----
        progress?.Invoke("扫描标准 HID 电量设备…");
        foreach (var iface in interfaces)
        {
            if (iface.VendorId is 0x046D or MchoseProvider.VendorId || iface.FeatureReportLength <= 0)
                continue;

            var id = StandardBatteryProvider.IdFor(iface);
            if (!seen.Add(id))
                continue;

            var reading = StandardBatteryProvider.TryRead(iface);
            if (reading == null)
                continue;

            candidates.Add(new DeviceCandidate(
                id,
                string.IsNullOrWhiteSpace(iface.Product) ? $"HID 设备 {id}" : iface.Product,
                GuessKind(iface),
                "HID",
                true,
                reading.Source));
        }

        return candidates
            .OrderByDescending(c => c.Supported)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static BatteryReading? ProbeMchose(HidInterface iface)
    {
        try
        {
            using var device = HidDevice.Open(iface, write: false);
            var report = device.GetFeature(0x06, iface.FeatureReportLength);
            return report == null ? null : MchoseProvider.Parse(report);
        }
        catch
        {
            return null;
        }
    }

    private static DeviceKind GuessKind(HidInterface iface) => iface.UsagePage switch
    {
        0x0001 when iface.Usage == 0x0006 => DeviceKind.Keyboard,
        0x0001 when iface.Usage == 0x0002 => DeviceKind.Mouse,
        0x000D => DeviceKind.Touchpad,
        _ => DeviceKind.Unknown
    };
}
