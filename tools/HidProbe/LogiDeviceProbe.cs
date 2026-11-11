using System.Text;
using BatteryWidget.Core.Hid;
using BatteryWidget.Core.Protocols;

namespace HidProbe;

/// <summary>对指定索引做详细的 HID++ 电量相关探测。</summary>
internal static class LogiDeviceProbe
{
    public static void Run(int index)
    {
        var sb = new StringBuilder();
        void W(string s) { Console.WriteLine(s); sb.AppendLine(s); }

        var interfaces = HidEnumerator.Enumerate(includeDescriptor: false);
        using var transport = HidppTransport.TryOpen(interfaces);
        if (transport == null)
        {
            W("打不开 HID++ 接口");
            return;
        }

        var protocol = transport.Ping(index, attempts: 4, timeoutMs: 1500);
        W($"索引 {index}: 协议 {protocol?.ToString("0.0") ?? "不可达"}");
        if (protocol == null) return;

        // 读取 0x1004 / 0x1000 / 0x0005 / 0x0003 的特性索引
        var features = new (ushort Id, string Name)[]
        {
            (0x1004, "UNIFIED_BATTERY"),
            (0x1000, "BATTERY_STATUS"),
            (0x1001, "BATTERY_VOLTAGE"),
            (0x0005, "DEVICE_NAME"),
            (0x0003, "DEVICE_FW_VERSION"),
            (0x1814, "CHANGE_HOST"),
        };

        var indices = new Dictionary<ushort, byte>();
        foreach (var (id, name) in features)
        {
            var idx = transport.GetFeature(index, id);
            W($"  getFeature(0x{id:X4}) {name} -> {(idx == null ? "不支持" : $"0x{idx:X2}")}");
            if (idx != null) indices[id] = idx.Value;
        }

        W("");
        // ADC 测量（0x1D4B）能给出电池电压，用来交叉验证电量百分比
        var adc = transport.GetFeature(index, 0x1D4B);
        if (adc != null)
        {
            for (var fn = 0; fn <= 2; fn++)
            {
                var r = transport.CallFeature(index, adc.Value, (byte)fn, ReadOnlySpan<byte>.Empty, 1000);
                W($"  0x1D4B fn{fn} -> {Format(r)}");
                if (r is { Ok: true, Data: { Length: >= 2 } d })
                {
                    var mv = d[0] | (d[1] << 8);
                    W($"    按小端 u16 解释: {mv} mV");
                }
            }
        }
        else
        {
            W("  0x1D4B ADC_MEASUREMENT 不支持");
        }


        if (indices.TryGetValue(0x1004, out var u))
        {
            for (var fn = 0; fn <= 3; fn++)
            {
                var r = transport.CallFeature(index, u, (byte)fn, ReadOnlySpan<byte>.Empty, 1200);
                W($"  0x1004 fn{fn} -> {Format(r)}");
                Thread.Sleep(150);
            }

            for (var i = 0; i < 3; i++)
            {
                var r = transport.CallFeature(index, u, 0x00, ReadOnlySpan<byte>.Empty, 1200);
                W($"  0x1004 fn0 第{i + 1}次 -> {Format(r)}");
                if (r is { Ok: true, Data: { } d })
                    W($"    解码: level={d[0]}, levelEnum=0x{(d.Length > 1 ? d[1] : 0):X2}, status=0x{(d.Length > 2 ? d[2] : 0):X2}");
                Thread.Sleep(600);
            }
        }

        // 枚举 FEATURE_SET (0x0001)，看看设备到底支持哪些特性
        var featureSet = transport.GetFeature(index, 0x0001);
        W($"  getFeature(0x0001) FEATURE_SET -> {(featureSet == null ? "不支持" : $"0x{featureSet:X2}")}");
        if (featureSet != null)
        {
            var countReply = transport.CallFeature(index, featureSet.Value, 0x00, ReadOnlySpan<byte>.Empty, 1000);
            W($"    fn0 getCount -> {Format(countReply)}");
            if (countReply is { Ok: true, Data.Length: >= 1 })
            {
                var count = countReply.Value.Data![0];
                W($"    共 {count} 个特性：");
                for (var i = 0; i < count; i++)
                {
                    var reply = transport.CallFeature(index, featureSet.Value, 0x01, new byte[] { (byte)i }, 900);
                    if (reply is { Ok: true, Data.Length: >= 2 })
                    {
                        var data = reply.Value.Data!;
                        var id = (ushort)((data[0] << 8) | data[1]);
                        W($"      #{i,-3} 0x{id:X4}  {(KnownFeatureName(id))}");
                    }
                    else
                    {
                        W($"      #{i,-3} <读取失败>");
                    }
                }
            }
        }

        if (indices.TryGetValue(0x1000, out var s))
        {
            var r = transport.CallFeature(index, s, 0x00, ReadOnlySpan<byte>.Empty, 1200);
            W($"  0x1000 fn0 -> {Format(r)}");
            if (r is { Ok: true, Data: { Length: >= 5 } d })
                W($"    解码: level={d[0]}, next={d[1]}, status=0x{d[2]:X2}, voltage={d[3] | (d[4] << 8)}mV");
        }

        if (indices.TryGetValue(0x1001, out var v))
        {
            var r = transport.CallFeature(index, v, 0x00, ReadOnlySpan<byte>.Empty, 1200);
            W($"  0x1001 fn0 -> {Format(r)}");
            if (r is { Ok: true, Data: { Length: >= 2 } d })
                W($"    电压: {d[0] | (d[1] << 8)} mV");
        }

        if (indices.TryGetValue(0x0005, out var n))
        {
            var count = transport.CallFeature(index, n, 0x00, ReadOnlySpan<byte>.Empty, 900);
            W($"  0x0005 fn0 (getCount) -> {Format(count)}");
            for (var offset = 0; offset < 8; offset += 4)
            {
                var chunk = transport.CallFeature(index, n, 0x01, new[] { (byte)offset }, 900);
                W($"  0x0005 fn1 offset={offset} -> {Format(chunk)}");
                if (chunk is not { Ok: true }) break;
            }
        }

        if (indices.TryGetValue(0x0003, out var fw))
        {
            var count = transport.CallFeature(index, fw, 0x00, ReadOnlySpan<byte>.Empty, 900);
            W($"  0x0003 fn0 (getEntityCount) -> {Format(count)}");
            var info = transport.CallFeature(index, fw, 0x01, new byte[] { 0x00 }, 900);
            W($"  0x0003 fn1 (entity 0) -> {Format(info)}");
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "logi-dev.log"), sb.ToString(), new UTF8Encoding(false));
    }

    private static string KnownFeatureName(ushort id) => id switch
    {
        0x0000 => "ROOT",
        0x0001 => "FEATURE_SET",
        0x0003 => "DEVICE_FW_VERSION",
        0x0005 => "DEVICE_NAME",
        0x0007 => "DEVICE_CAPABILITIES",
        0x1000 => "BATTERY_STATUS",
        0x1001 => "BATTERY_VOLTAGE",
        0x1004 => "UNIFIED_BATTERY",
        0x1005 => "BATTERY_VOLTAGE?",
        0x1007 => "BATTERY?",
        0x1814 => "CHANGE_HOST",
        0x1815 => "HOSTS_INFO",
        0x1D4B => "ADC_MEASUREMENT",
        0x1982 => "BACKLIGHT2",
        0x8040 => "?",
        _ => ""
    };

    private static string Format(HidppReply? reply)
    {
        if (reply == null) return "<无回复>";
        if (reply.Value.IsError) return $"<错误 0x{reply.Value.ErrorCode:X2}: {reply.Value.Message}>";
        return reply.Value.Data == null ? "<空>" : string.Join(' ', reply.Value.Data.Select(b => b.ToString("X2")));
    }
}
