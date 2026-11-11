using System.Text;
using BatteryWidget.Core.Hid;

namespace HidProbe;

/// <summary>HID++ 诊断实验（罗技接收器 / 配对设备 / 1.0 寄存器）。</summary>
internal static class LogiExperiment
{
    private static readonly StringBuilder Sb = new();
    private static string _logPath = "";

    public static void Run()
    {
        RunCore(wake: false);
    }

    public static void RunWake()
    {
        RunCore(wake: true);
    }

    private static void RunCore(bool wake)
    {
        _logPath = Path.Combine(AppContext.BaseDirectory, "logi.log");
        try
        {
            var interfaces = HidEnumerator.Enumerate(includeDescriptor: false);
            var shortIface = interfaces.FirstOrDefault(i => i.VendorId == 0x046D && i.UsagePage == 0xFF00 && i.Usage == 0x0001);
            var longIface = interfaces.FirstOrDefault(i => i.VendorId == 0x046D && i.UsagePage == 0xFF00 && i.Usage == 0x0002);
            if (shortIface == null)
            {
                W("找不到 HID++ 短报文接口");
                return;
            }

            using var sh = HidDevice.Open(shortIface, write: true);
            HidDevice? lg = null;
            if (longIface != null)
            {
                try { lg = HidDevice.Open(longIface, write: true); } catch (Exception ex) { W("长报文接口打开失败: " + ex.Message); }
            }

            W($"短接口 in={shortIface.InputReportLength} out={shortIface.OutputReportLength}");
            if (lg != null) W($"长接口 in={lg.Interface.InputReportLength} out={lg.Interface.OutputReportLength}");

            if (wake)
            {
                W("");
                W("## 唤醒尝试：对每个索引重复 ping 8 次");
                foreach (var idx in new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 })
                {
                    for (var attempt = 0; attempt < 8; attempt++)
                    {
                        var mark = (byte)Random.Shared.Next(0x10, 0xEF);
                        var ping = new byte[] { 0x00, 0x1B, 0x00, 0x00, mark };
                        var replies = Exchange(sh, lg, idx, ping, 400);
                        W($"  idx 0x{idx:X2} 第{attempt + 1}次 -> {Format(replies)}");
                        Thread.Sleep(200);
                    }
                }

                W("");
                W("## 唤醒后读取电量");
                foreach (var idx in new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 })
                {
                    // 2.0 路径
                    var getFeature = new byte[] { 0x00, 0x0B, 0x10, 0x04 };
                    var featureReplies = Exchange(sh, lg, idx, getFeature, 500);
                    W($"  idx 0x{idx:X2} getFeature(0x1004) -> {Format(featureReplies)}");
                    foreach (var (source, data) in featureReplies)
                    {
                        if (data.Length >= 6 && data[2] == 0x00)
                        {
                            var featureIndex = data[4];
                            var battery = Exchange(sh, lg, idx, new byte[] { featureIndex, 0x0B }, 500);
                            W($"    UNIFIED_BATTERY -> {Format(battery)}");
                        }
                    }

                    // 1.0 路径
                    foreach (var reg in new byte[] { 0x07, 0x0D })
                    {
                        var regReplies = Exchange(sh, lg, idx, new byte[] { 0x81, reg }, 500);
                        W($"  idx 0x{idx:X2} reg 0x{reg:X2} -> {Format(regReplies)}");
                    }
                }
                return;
            }

            W("");
            W("## A. 每个索引 ping（ROOT.getProtocolVersion）");
            foreach (var idx in new byte[] { 0xFF, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 })
            {
                var mark = (byte)Random.Shared.Next(0x10, 0xEF);
                var data = new byte[] { 0x00, 0x1B, 0x00, 0x00, mark };
                var replies = Exchange(sh, lg, idx, data, 700);
                W($"  idx 0x{idx:X2} ping -> {Format(replies)}");
            }

            W("");
            W("## B. 接收器信息寄存器 0xB5（长寄存器读）");
            foreach (var sub in new byte[] { 0x03, 0x20, 0x21, 0x22, 0x40, 0x41, 0x50, 0x51, 0x60, 0x61 })
            {
                var data = new byte[] { 0x83, 0xB5, sub };
                var replies = Exchange(sh, lg, 0xFF, data, 800);
                W($"  0xB5/0x{sub:X2} -> {Format(replies)}");
            }

            W("");
            W("## C. 每个索引的特征探测（feature 0x1004 / 0x1000 / 0x0005）");
            foreach (var idx in new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 })
            {
                foreach (var feature in new ushort[] { 0x1004, 0x1000, 0x0005 })
                {
                    var data = new byte[] { 0x00, 0x0B, (byte)(feature >> 8), (byte)feature };
                    var replies = Exchange(sh, lg, idx, data, 700);
                    W($"  idx 0x{idx:X2} getFeature(0x{feature:X4}) -> {Format(replies)}");
                }
            }

            W("");
            W("## D. HID++ 1.0 电量寄存器（0x07 / 0x0D）");
            foreach (var idx in new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 })
            {
                foreach (var reg in new byte[] { 0x07, 0x0D })
                {
                    var data = new byte[] { 0x81, reg };
                    var replies = Exchange(sh, lg, idx, data, 700);
                    W($"  idx 0x{idx:X2} reg 0x{reg:X2} -> {Format(replies)}");
                }
            }
        }
        catch (Exception ex)
        {
            W("异常: " + ex);
        }
        finally
        {
            try { File.WriteAllText(_logPath, Sb.ToString(), new UTF8Encoding(false)); } catch { }
            Console.WriteLine("日志: " + _logPath);
        }
    }

    private static List<(string Source, byte[] Data)> Exchange(HidDevice sh, HidDevice? lg, byte deviceIndex, byte[] data, int timeoutMs)
    {
        var result = new List<(string, byte[])>();
        sh.Drain(32, 3);
        lg?.Drain(32, 3);

        var message = new byte[7];
        message[0] = 0x10;
        message[1] = deviceIndex;
        Array.Copy(data, 0, message, 2, Math.Min(data.Length, 5));
        if (!sh.TryWriteOutput(message, 500, out _))
            return result;

        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var remaining = (int)Math.Max(1, Math.Min(150, deadline - Environment.TickCount64));

            var r1 = sh.ReadInput(remaining);
            if (r1 != null)
                result.Add(("short", r1));

            if (lg != null)
            {
                var r2 = lg.ReadInput(1);
                if (r2 != null)
                    result.Add(("long", r2));
            }

            if (result.Count > 0 && Environment.TickCount64 > deadline - timeoutMs + 200)
                break;
        }
        return result;
    }

    private static string Format(List<(string Source, byte[] Data)> replies)
    {
        if (replies.Count == 0) return "<无回复>";
        return string.Join(" | ", replies.Select(r => $"[{r.Source}] {ReportDescriptorParser.ToHex(r.Data)}"));
    }

    private static void W(string text)
    {
        Console.WriteLine(text);
        Sb.AppendLine(text);
    }
}
