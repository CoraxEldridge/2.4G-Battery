using System.Text;
using BatteryWidget.Core.Hid;

namespace HidProbe;

/// <summary>
/// 一次性诊断工具。用法：
///   HidProbe.exe                 完整报告
///   HidProbe.exe logi-raw        罗技接收器原始收包诊断
///   HidProbe.exe mchose-bat      反复读取迈从电量并触发推送
/// </summary>
internal static class Program
{
    private static readonly StringBuilder Log = new();
    private static string _logPath = "";

    private static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        _logPath = Path.Combine(AppContext.BaseDirectory, "probe.log");
        try
        {
            var mode = args.Length > 0 ? args[0] : "full";
            switch (mode)
            {
                case "logi-raw":
                case "logi":
                    LogiExperiment.Run();
                    break;
                case "batt":
                    RunBattery();
                    break;
                case "wake":
                    LogiExperiment.RunWake();
                    break;
                case "read":
                    RunRead();
                    break;
                case "dev":
                    LogiDeviceProbe.Run(args.Length > 1 ? int.Parse(args[1]) : 4);
                    break;
                case "mchose-raw2":
                    MchoseChannelProbe.Run();
                    break;
                case "mchose-bat":
                    RunMchoseBattery();
                    break;
                default:
                    RunFull();
                    break;
            }
        }
        catch (Exception ex)
        {
            WriteLine("FATAL: " + ex);
        }
        finally
        {
            WriteLine("");
            WriteLine("日志已写入: " + _logPath);
            try { File.WriteAllText(_logPath, Log.ToString(), new UTF8Encoding(false)); } catch { }
        }
    }

    private static void WriteLine(string text)
    {
        Console.WriteLine(text);
        Log.AppendLine(text);
    }

    private static List<HidInterface> Enumerate() => HidEnumerator.Enumerate(includeDescriptor: false);

    private static void RunBattery()
    {
        var ids = new HashSet<string>(new[] { "logitech:1", "logitech:2", "logitech:3", "logitech:4", "logitech:5", "logitech:6" });
        var request = new BatteryWidget.Core.Devices.DeviceReadRequest(true, ids);
        var provider = new BatteryWidget.Core.Protocols.LogitechProvider();
        foreach (var snapshot in provider.Read(request))
        {
            Console.WriteLine($"{snapshot.Id}  {snapshot.Name}");
            Console.WriteLine($"  在线={snapshot.Online}  电量={snapshot.Battery?.Percent?.ToString() ?? "-"}  状态={snapshot.Battery?.Status}  ({snapshot.Battery?.StatusText})");
            Console.WriteLine($"  原始={snapshot.Battery?.Detail ?? snapshot.Detail ?? "-"}");
        }

        // 额外：读 0x1000（旧版电池状态特征），看它的状态字节
        var interfaces = BatteryWidget.Core.Hid.HidEnumerator.Enumerate(includeDescriptor: false);
        using var transport = BatteryWidget.Core.Protocols.HidppTransport.TryOpen(interfaces);
        if (transport != null)
        {
            var feature = transport.GetFeature(4, 0x1000);
            Console.WriteLine($"  feature 0x1000 索引 = {(feature.HasValue ? "0x" + feature.Value.ToString("X2") : "无")}");
            if (feature.HasValue)
            {
                var reply = transport.CallFeature(4, feature.Value, 0x00, ReadOnlySpan<byte>.Empty, 1200);
                var text = reply is { Ok: true, Data: { } raw } ? string.Join(' ', raw.Select(b => b.ToString("X2"))) : "无回复";
                Console.WriteLine($"  0x1000 fn0 = {text}");
            }
        }
    }

    private static void RunRead()
    {
        WriteLine("# 设备电量读取（正式代码路径）");
        var service = new BatteryWidget.Core.Services.DeviceService();
        var started = DateTime.Now;
        var results = service.ReadAll(new BatteryWidget.Core.Devices.DeviceReadRequest(true, null));
        WriteLine($"耗时 {(DateTime.Now - started).TotalMilliseconds:0} ms，共 {results.Count} 条");
        foreach (var d in results)
        {
            WriteLine("");
            WriteLine($"## {d.Name}  ({d.Id})");
            WriteLine($"    类型: {d.KindText}  连接: {d.Transport}  在线: {d.Online}");
            WriteLine($"    详情: {d.Detail}");
            if (d.Error != null) WriteLine($"    错误: {d.Error}");
            if (d.Battery != null)
            {
                WriteLine($"    电量: {d.Battery.Percent?.ToString() ?? "?"}%  {d.Battery.StatusText}");
                WriteLine($"    来源: {d.Battery.Source}");
                WriteLine($"    原始: {d.Battery.Detail}");
                if (d.Battery.VoltageMillivolts != null) WriteLine($"    电压: {d.Battery.VoltageMillivolts} mV");
            }
            else
            {
                WriteLine("    电量: 未读到");
            }
        }
    }

    // ================================================================ 完整报告

    private static void RunFull()
    {
        WriteLine($"# HID 探测报告  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        WriteLine("");

        var interfaces = Enumerate();
        WriteLine("## 1. 全部 HID 接口");
        foreach (var i in interfaces.OrderBy(x => x.VendorId).ThenBy(x => x.ProductId).ThenBy(x => x.UsagePage))
        {
            WriteLine($"{i.VidPid}  usage=0x{i.UsagePage:X4}/0x{i.Usage:X4}  in={i.InputReportLength,-4} out={i.OutputReportLength,-4} feat={i.FeatureReportLength,-4}  product='{i.Product}' mfr='{i.Manufacturer}'");
            WriteLine($"    path={i.Path}");
        }
        WriteLine("");

        var targets = interfaces.Where(i => i.VendorId is 0x41E4 or 0x046D or 0x0461).ToList();

        WriteLine("## 2. 迈从 41E4 各接口 Feature Report 转储");
        foreach (var i in targets.Where(x => x.VendorId == 0x41E4 && x.FeatureReportLength > 0))
        {
            WriteLine($"### usage=0x{i.UsagePage:X4}/0x{i.Usage:X4} feat={i.FeatureReportLength}");
            using var dev = HidDevice.Open(i, write: false);
            for (var rid = 0; rid <= 0x10; rid++)
            {
                var data = dev.GetFeature(rid, i.FeatureReportLength);
                if (data != null)
                    WriteLine($"  RID 0x{rid:X2}: {Hex(data)}");
            }
        }
        WriteLine("");

        WriteLine("## 3. 迈从 41E4 配置通道原始探测");
        foreach (var i in targets.Where(x => x.VendorId == 0x41E4 && x.OutputReportLength > 0 && x.UsagePage == 0xFF02))
        {
            MchoseRaw(i);
        }
    }

    private static void MchoseRaw(HidInterface iface)
    {
        WriteLine($"### usage=0x{iface.UsagePage:X4} out={iface.OutputReportLength}");
        using var dev = HidDevice.Open(iface, write: true);

        WriteLine("  监听 1500ms ...");
        Listen(dev, 1500);

        var payloads = new (string Name, byte Cmd, byte Arg)[]
        {
            ("firmware", 0x04, 0x00),
            ("perf-dump", 0x67, 0x00),
            ("lighting-read", 0x1B, 0x00),
            ("buttons", 0x03, 0x00),
        };

        foreach (var (name, cmd, arg) in payloads)
        {
            var msg = new byte[iface.OutputReportLength];
            msg[0] = 0x13;
            msg[1] = (byte)(cmd ^ 0xFF);
            msg[2] = (byte)(arg ^ 0xFF);
            WriteLine($"  -- {name}：写 0x{cmd:X2} --");
            if (!dev.TryWriteOutput(msg, 500, out var err))
            {
                WriteLine($"    写失败 {err}");
                continue;
            }
            Listen(dev, 2000);
        }
    }

    // ================================================================ 罗技

    private static void RunLogitechRaw()
    {
        WriteLine("# 罗技接收器原始收包诊断");
        var interfaces = Enumerate().Where(i => i.VendorId == 0x046D && i.UsagePage == 0xFF00).ToList();
        foreach (var iface in interfaces)
        {
            WriteLine("");
            WriteLine($"### usage=0x{iface.UsagePage:X4}/0x{iface.Usage:X4} in={iface.InputReportLength} out={iface.OutputReportLength}");
            using var dev = HidDevice.Open(iface, write: true);

            var reportId = iface.Usage == 0x0001 ? (byte)0x10 : (byte)0x11;
            var length = iface.OutputReportLength;

            foreach (var index in new byte[] { 0xFF, 0x00, 0x01, 0x02 })
            {
                var msg = new byte[length];
                msg[0] = reportId;
                msg[1] = index;
                msg[2] = 0x00;
                msg[3] = 0x01;
                msg[4] = 0x10; // feature 0x1004 高字节
                msg[5] = 0x04;
                WriteLine($"  -- index 0x{index:X2} report 0x{reportId:X2} --");
                if (!dev.TryWriteOutput(msg, 500, out var err))
                {
                    WriteLine($"    写失败 {err}");
                    continue;
                }
                Listen(dev, 1200);
            }

            // 尝试 HID++ 1.0 风格：feature 索引 0x00，function 0x00
            for (var sw = 0; sw <= 0x0F; sw += 0x0F)
            {
                var msg = new byte[length];
                msg[0] = reportId;
                msg[1] = 0xFF;
                msg[2] = 0x00;
                msg[3] = (byte)(0x00 | sw);
                WriteLine($"  -- root 探测 swId=0x{sw:X2} --");
                dev.TryWriteOutput(msg, 500, out _);
                Listen(dev, 1000);
            }
        }
    }

    // ================================================================ 迈从电量

    private static void RunMchoseBattery()
    {
        WriteLine("# 迈从电量实验");
        var interfaces = Enumerate();
        var featureIface = interfaces.FirstOrDefault(i => i.VendorId == 0x41E4 && i.UsagePage == 0xFF04);
        var configIface = interfaces.FirstOrDefault(i => i.VendorId == 0x41E4 && i.UsagePage == 0xFF02);

        if (featureIface != null)
        {
            WriteLine($"## Feature 接口 RID 0x06（长度 {featureIface.FeatureReportLength}）");
            using var dev = HidDevice.Open(featureIface, write: false);
            for (var i = 0; i < 5; i++)
            {
                var data = dev.GetFeature(0x06, featureIface.FeatureReportLength);
                WriteLine($"  第 {i + 1} 次: {Hex(data)}   （解码: {DecodeBatteryFeature(data)}）");
                Thread.Sleep(400);
            }
        }

        if (configIface != null)
        {
            WriteLine("");
            WriteLine("## 配置通道：发 0x0B 0xAA（重载配置）触发电量推送");
            using var dev = HidDevice.Open(configIface, write: true);
            var msg = new byte[configIface.OutputReportLength];
            msg[0] = 0x13;
            msg[1] = (byte)(0x0B ^ 0xFF);
            msg[2] = (byte)(0xAA ^ 0xFF);
            WriteLine($"  写: {Hex(msg)}");
            if (dev.TryWriteOutput(msg, 500, out var err))
            {
                WriteLine("  已发送，监听 5 秒（请随意按几下键盘）...");
                Listen(dev, 5000);
            }
            else
            {
                WriteLine($"  写失败 {err}");
            }
        }
    }

    private static string DecodeBatteryFeature(byte[]? data)
    {
        if (data == null || data.Length < 4)
            return "无数据";
        var sb = new StringBuilder();
        for (var i = 1; i < data.Length; i++)
            sb.Append($"[{i}]={data[i],3}(0x{data[i]:X2}) ");
        return sb.ToString();
    }

    // ================================================================ 工具

    private static void Listen(HidDevice dev, int totalMs)
    {
        var deadline = Environment.TickCount64 + totalMs;
        while (Environment.TickCount64 < deadline)
        {
            var reply = dev.ReadInput((int)Math.Max(1, Math.Min(250, deadline - Environment.TickCount64)));
            if (reply == null) continue;
            WriteLine($"    IN: {Hex(reply)}");
            if (reply.Length >= 2)
            {
                var xorFirst = (byte)(reply[1] ^ 0xFF);
                if (xorFirst is 0xE2 or 0xE5)
                    WriteLine($"      ^^ 疑似迈从推送（XOR 后 0x{xorFirst:X2}）");
            }
        }
    }

    private static string Hex(byte[]? data) => data == null ? "<无回复>" : ReportDescriptorParser.ToHex(data);
}
