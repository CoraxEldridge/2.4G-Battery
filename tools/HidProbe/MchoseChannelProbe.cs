using System.Text;
using BatteryWidget.Core.Hid;

namespace HidProbe;

/// <summary>迈从配置通道（0xFF02 / RID 0x13）原始字节探测：只发只读命令。</summary>
internal static class MchoseChannelProbe
{
    public static void Run()
    {
        var sb = new StringBuilder();
        void W(string s) { Console.WriteLine(s); sb.AppendLine(s); }

        var interfaces = HidEnumerator.Enumerate(includeDescriptor: false);
        var iface = interfaces.FirstOrDefault(i => i.VendorId == 0x41E4 && i.UsagePage == 0xFF02);
        if (iface == null)
        {
            W("找不到 0xFF02 配置通道");
            return;
        }

        W($"配置通道 out={iface.OutputReportLength} in={iface.InputReportLength}");
        using var dev = HidDevice.Open(iface, write: true);

        var payloads = new (string Name, byte Cmd, byte Arg)[]
        {
            ("固件 0x04", 0x04, 0x00),
            ("按键 0x03", 0x03, 0x00),
            ("宏 0x06", 0x06, 0x00),
            ("灯效 0x1B", 0x1B, 0x00),
            ("性能 0x67", 0x67, 0x00),
            ("0x68", 0x68, 0x00),
        };

        foreach (var mode in new[] { "raw", "xor" })
        {
            W("");
            W($"## {mode} 模式");
            foreach (var (name, cmd, arg) in payloads)
            {
                W($"  -- {name} --");
                Send(dev, iface.OutputReportLength, mode == "xor",
                     new[] { cmd, arg }, W);
                Thread.Sleep(120);
            }
        }

        // 额外：RID 0x13 + 两字节长度头
        W("");
        W("## 带长度头（[0x13, len, cmd, arg]）");
        Send(dev, iface.OutputReportLength, false, new byte[] { 0x02, 0x04, 0x00 }, W);

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "mchose-channel.log"), sb.ToString(),
            new UTF8Encoding(false));
    }

    private static void Send(HidDevice dev, int outLength, bool xor, byte[] payload, Action<string> log)
    {
        var message = new byte[outLength];
        message[0] = 0x13;
        for (var i = 0; i < payload.Length; i++)
            message[i + 1] = xor ? (byte)(payload[i] ^ 0xFF) : payload[i];

        dev.Drain(64, 3);
        if (!dev.TryWriteOutput(message, 400, out var err))
        {
            log($"    写失败 Win32={err}");
            return;
        }
        log($"    OUT: {Hex(message)}");

        var deadline = Environment.TickCount64 + 1500;
        while (Environment.TickCount64 < deadline)
        {
            var reply = dev.ReadInput((int)Math.Max(1, Math.Min(250, deadline - Environment.TickCount64)));
            if (reply == null) continue;
            log($"    IN : {Hex(reply)}");
        }
    }

    private static string Hex(byte[] data) => string.Join(' ', data.Select(b => b.ToString("X2")));
}
