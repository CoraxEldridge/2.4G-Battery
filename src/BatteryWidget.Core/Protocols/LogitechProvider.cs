using System.Text;
using BatteryWidget.Core.Devices;
using BatteryWidget.Core.Hid;

namespace BatteryWidget.Core.Protocols;

/// <summary>通过罗技 HID++ 接收器读取已配对设备的电量。</summary>
public sealed class LogitechProvider : IDeviceProvider
{
    public string Name => "罗技 HID++";

    private const ushort FeatureDeviceName = 0x0005;
    private const ushort FeatureBatteryStatus = 0x1000;
    private const ushort FeatureUnifiedBattery = 0x1004;

    public static string IdFor(int index) => $"logitech:{index}";

    public IReadOnlyList<DeviceSnapshot> Read(DeviceReadRequest request)
    {
        var attempts = request.DeepWake ? 4 : 1;
        var timeout = request.DeepWake ? 1300 : 650;
        var snapshots = new List<DeviceSnapshot>();
        var interfaces = HidEnumerator.Enumerate(includeDescriptor: false);
        var receiver = interfaces.FirstOrDefault(i => i.VendorId == 0x046D);
        if (receiver == null)
            return snapshots;

        using var transport = HidppTransport.TryOpen(interfaces);
        if (transport == null)
            return snapshots;

        var hasTouchpadCollection = interfaces.Any(i => i.VendorId == 0x046D && i.UsagePage == 0x000D);

        for (var index = 1; index <= 6; index++)
        {
            var id = IdFor(index);
            if (!request.Includes(id))
                continue;

            var protocol = transport.Ping(index, attempts: attempts, timeoutMs: timeout);
            if (protocol == null)
                continue;

            if (protocol < 2.0)
            {
                snapshots.Add(ReadHidpp10(transport, index, id));
                continue;
            }

            var deviceName = ReadDeviceName(transport, index);
            var kind = GuessKind(deviceName, hasTouchpadCollection);
            var battery = ReadBattery20(transport, index);
            snapshots.Add(new DeviceSnapshot(
                id,
                string.IsNullOrWhiteSpace(deviceName) ? "罗技设备" : deviceName,
                kind,
                "2.4G",
                battery,
                Online: battery != null,
                Detail: $"HID++ {protocol:0.0} · 接收器索引 {index}"));
        }

        return snapshots;
    }

    /// <summary>扫描接收器上所有能响应的配对设备（供设备选择界面使用）。</summary>
    public static IReadOnlyList<DeviceSnapshot> ScanPairedDevices(IEnumerable<HidInterface>? interfaces = null)
    {
        var list = interfaces?.ToList() ?? HidEnumerator.Enumerate(includeDescriptor: false);
        var result = new List<DeviceSnapshot>();
        if (!list.Any(i => i.VendorId == 0x046D))
            return result;

        using var transport = HidppTransport.TryOpen(list);
        if (transport == null)
            return result;

        var hasTouchpadCollection = list.Any(i => i.VendorId == 0x046D && i.UsagePage == 0x000D);
        for (var index = 1; index <= 6; index++)
        {
            var protocol = transport.Ping(index, attempts: 3, timeoutMs: 1200);
            if (protocol == null)
                continue;

            var name = protocol >= 2.0 ? ReadDeviceName(transport, index) : "";
            var kind = GuessKind(name, hasTouchpadCollection);
            result.Add(new DeviceSnapshot(
                IdFor(index),
                string.IsNullOrWhiteSpace(name) ? $"罗技设备（索引 {index}）" : name,
                kind,
                "2.4G",
                protocol >= 2.0 ? ReadBattery20(transport, index) : null,
                Online: true,
                Detail: $"HID++ {protocol:0.0}"));
        }
        return result;
    }

    private static DeviceSnapshot ReadHidpp10(HidppTransport transport, int index, string id)
    {
        var battery = ReadBattery10(transport, index);
        return new DeviceSnapshot(id, "罗技无线设备", DeviceKind.Unknown, "2.4G", battery, battery != null,
            "HID++ 1.0 寄存器");
    }

    private static BatteryReading? ReadBattery10(HidppTransport transport, int index)
    {
        // 先试 0x0D（直接给百分比），再试 0x07（只有近似档位）
        var charge = transport.ReadRegister(index, 0x0D, 800);
        if (charge is { Ok: true, Data.Length: >= 1 })
        {
            var data = charge.Value.Data!;
            var percent = data[0] <= 100 ? data[0] : (int?)null;
            var status = BatteryStatus.Unknown;
            if (data.Length >= 3)
            {
                status = (byte)(data[2] & 0xF0) switch
                {
                    0x30 => BatteryStatus.Discharging,
                    0x50 => BatteryStatus.Charging,
                    0x90 => BatteryStatus.Full,
                    _ => BatteryStatus.Unknown
                };
            }
            return new BatteryReading(percent, status, null, "HID++ 1.0 0x0D 电量",
                $"原始: {string.Join(' ', data.Select(b => b.ToString("X2")))}");
        }

        var statusReply = transport.ReadRegister(index, 0x07, 800);
        if (statusReply is { Ok: true, Data.Length: >= 2 })
        {
            var data = statusReply.Value.Data!;
            var level = data[0] switch
            {
                7 => 100,
                5 => 70,
                3 => 30,
                1 => 5,
                _ => (int?)null
            };
            var charging = data[1];
            var status = charging switch
            {
                0x00 => BatteryStatus.Discharging,
                var c when (c & 0x21) == 0x21 => BatteryStatus.Charging,
                var c when (c & 0x22) == 0x22 => BatteryStatus.Full,
                _ => BatteryStatus.Unknown
            };
            return new BatteryReading(level, status, null, "HID++ 1.0 0x07 电池状态",
                $"状态字节 0x{data[0]:X2} 充电字节 0x{data[1]:X2}");
        }

        return null;
    }

    private static string ReadDeviceName(HidppTransport transport, int index)
    {
        var featureIndex = transport.GetFeature(index, FeatureDeviceName);
        if (featureIndex == null)
            return "";

        var countReply = transport.CallFeature(index, featureIndex.Value, 0x00, ReadOnlySpan<byte>.Empty);
        if (countReply is not { Ok: true, Data.Length: >= 1 })
            return "";

        var count = countReply.Value.Data![0];
        if (count is 0 or > 64)
            return "";

        var builder = new StringBuilder();
        var offset = 0;
        while (offset < count && builder.Length < count)
        {
            var chunk = transport.CallFeature(index, featureIndex.Value, 0x01, new[] { (byte)offset }, 700);
            if (chunk is not { Ok: true, Data.Length: >= 1 })
                break;

            var data = chunk.Value.Data!;
            // 多数设备回显 offset 字节；该字节一定不可打印，可与名字首字符区分
            var start = data.Length > 0 && data[0] == offset && data[0] < 0x20 ? 1 : 0;

            var added = 0;
            for (var i = start; i < data.Length; i++)
            {
                var b = data[i];
                if (b == 0)
                    break;
                if (b is < 0x20 or > 0x7E)
                    break;
                builder.Append((char)b);
                added++;
            }

            if (added == 0)
                break;
            offset += added;
        }

        return builder.ToString().Trim();
    }

    private static BatteryReading? ReadBattery20(HidppTransport transport, int index)
    {
        var unified = transport.GetFeature(index, FeatureUnifiedBattery);
        if (unified != null)
        {
            // 该机型（Pop-Up Touch）的 fn0 第一字节不是百分比（实测 0x0F=15），
            // fn1 才是真实电量（0x46=70，与官方软件一致）；fn0 的第 3 字节仍是充电状态。
            var statusReply = transport.CallFeature(index, unified.Value, 0x00, ReadOnlySpan<byte>.Empty, 1100);
            var levelReply = transport.CallFeature(index, unified.Value, 0x01, ReadOnlySpan<byte>.Empty, 1100);

            int? percent = null;
            if (levelReply is { Ok: true, Data.Length: >= 1 } && levelReply.Value.Data![0] <= 100)
                percent = levelReply.Value.Data![0];

            var status = BatteryStatus.Unknown;
            if (statusReply is { Ok: true, Data.Length: >= 3 })
            {
                status = MapUnifiedStatus(statusReply.Value.Data![2]);
                percent ??= statusReply.Value.Data![0];
            }

            if (percent != null)
            {
                var raw = $"fn0={(statusReply?.Data == null ? "-" : Format(statusReply.Value.Data))} " +
                          $"fn1={(levelReply?.Data == null ? "-" : Format(levelReply.Value.Data))}";
                return new BatteryReading(percent, status, null, "HID++ 0x1004 统一电池", raw);
            }
        }

        var status2 = transport.GetFeature(index, FeatureBatteryStatus);
        if (status2 != null)
        {
            var reply = transport.CallFeature(index, status2.Value, 0x00, ReadOnlySpan<byte>.Empty, 1000);
            if (reply is { Ok: true, Data.Length: >= 3 })
            {
                // 布局：[放电电量, 下一档电量, 状态, 电压低字节, 电压高字节]
                var data = reply.Value.Data!;
                int? voltage = data.Length >= 5 ? data[3] | (data[4] << 8) : null;
                if (voltage is < 2000 or > 5000)
                    voltage = null;
                return new BatteryReading(
                    Math.Clamp((int)data[0], 0, 100),
                    MapUnifiedStatus(data[2]),
                    voltage,
                    "HID++ 0x1000 电池状态",
                    $"原始: {Format(data)}");
            }
        }

        return null;
    }

    private static BatteryStatus MapUnifiedStatus(byte status) => status switch
    {
        0 => BatteryStatus.Discharging,
        1 or 2 or 4 => BatteryStatus.Charging,
        3 => BatteryStatus.Full,
        _ => BatteryStatus.Unknown
    };

    private static DeviceKind GuessKind(string name, bool receiverHasTouchpad)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("touch") || lower.Contains("t650") || lower.Contains("t651"))
            return DeviceKind.Touchpad;
        if (lower.Contains("keyboard") || lower.Contains("keys") || lower.Contains("k380") || lower.Contains("k400"))
            return DeviceKind.Keyboard;
        if (lower.Contains("mouse") || lower.Contains("mx ") || lower.Contains("m "))
            return DeviceKind.Mouse;
        return receiverHasTouchpad ? DeviceKind.Touchpad : DeviceKind.Unknown;
    }

    private static string Format(byte[] data) => string.Join(' ', data.Select(b => b.ToString("X2")));
}
