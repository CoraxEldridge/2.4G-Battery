using BatteryWidget.Core.Hid;

namespace BatteryWidget.Core.Protocols;

/// <summary>
/// HID++ 传输层：短报文（0xFF00/0x0001，7 字节）发请求，短/长报文（0xFF00/0x0002，20 字节）收回复。
/// 报文格式参考 pwr-Solaar 的 base.py。
/// </summary>
public sealed class HidppTransport : IDisposable
{
    /// <summary>软件 ID，避开 Options+(0x0D)、G HUB 等已占用值。</summary>
    public const byte SwId = 0x0B;

    private readonly HidDevice _short;
    private readonly HidDevice? _long;
    private bool _disposed;

    private HidppTransport(HidDevice shortDevice, HidDevice? longDevice)
    {
        _short = shortDevice;
        _long = longDevice;
    }

    public static HidppTransport? TryOpen(IEnumerable<HidInterface> interfaces)
    {
        var list = interfaces.ToList();
        var shortIface = list.FirstOrDefault(i => i.VendorId == 0x046D && i.UsagePage == 0xFF00 && i.Usage == 0x0001);
        var longIface = list.FirstOrDefault(i => i.VendorId == 0x046D && i.UsagePage == 0xFF00 && i.Usage == 0x0002);
        if (shortIface == null)
            return null;

        HidDevice? shortDevice = null;
        HidDevice? longDevice = null;
        try
        {
            shortDevice = HidDevice.Open(shortIface, write: true);
            if (longIface != null)
            {
                try { longDevice = HidDevice.Open(longIface, write: true); }
                catch { longDevice = null; }
            }
            return new HidppTransport(shortDevice, longDevice);
        }
        catch
        {
            shortDevice?.Dispose();
            longDevice?.Dispose();
            return null;
        }
    }

    /// <summary>发送 [featureIndex, function&lt;&lt;4|SwId, params...] 形式的 HID++ 2.0 请求。</summary>
    public HidppReply? Exchange(int deviceIndex, byte featureIndex, byte function, ReadOnlySpan<byte> parameters,
        int timeoutMs = 900)
    {
        var request = new byte[2 + Math.Min(3, parameters.Length)];
        request[0] = featureIndex;
        request[1] = (byte)((function << 4) | SwId);
        parameters[..Math.Min(3, parameters.Length)].CopyTo(request.AsSpan(2));
        return ExchangeRaw(deviceIndex, request, timeoutMs);
    }

    /// <summary>发送任意 data（最多 5 字节），等待匹配 request 前两字节的回复。</summary>
    public HidppReply? ExchangeRaw(int deviceIndex, byte[] data, int timeoutMs = 900)
    {
        ThrowIfDisposed();
        if (data.Length < 2)
            throw new ArgumentException("请求至少 2 字节", nameof(data));

        var message = new byte[7];
        message[0] = 0x10;
        message[1] = (byte)deviceIndex;
        Array.Copy(data, 0, message, 2, Math.Min(data.Length, 5));

        Drain();
        if (!_short.TryWriteOutput(message, 500, out var writeError))
            return new HidppReply(null, true, 0, $"写入失败 Win32={writeError}");

        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var remaining = (int)Math.Max(1, deadline - Environment.TickCount64);
            var reply = ReadAny(Math.Min(remaining, 120));
            if (reply == null)
                continue;

            var (_, device, payload) = reply.Value;
            if (device != deviceIndex && device != (byte)(deviceIndex ^ 0xFF))
                continue;
            if (payload.Length < 2)
                continue;

            if (payload[0] == 0x8F && payload.Length >= 4 && payload[1] == data[0] && payload[2] == data[1])
                return new HidppReply(null, true, payload[3], DescribeError(payload[3]));

            if (payload[0] == 0xFF && payload.Length >= 4 && payload[1] == data[0] && payload[2] == data[1])
                return new HidppReply(null, true, payload[3], $"HID++2.0 错误 0x{payload[3]:X2}");

            if (payload[0] == data[0] && payload[1] == data[1])
            {
                var rest = new byte[Math.Max(0, payload.Length - 2)];
                Array.Copy(payload, 2, rest, 0, rest.Length);
                return new HidppReply(rest, false, 0, "");
            }
        }

        return null;
    }

    /// <summary>HID++ 1.0 短寄存器读（请求 0x81 + 寄存器号）。</summary>
    public HidppReply? ReadRegister(int deviceIndex, byte register, int timeoutMs = 800)
        => ExchangeRaw(deviceIndex, new[] { (byte)0x81, register }, timeoutMs);

    /// <summary>HID++ 2.0 协议版本；1.0 设备返回 1.0；不可达返回 null。</summary>
    public double? Ping(int deviceIndex, int attempts = 3, int timeoutMs = 1400)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var mark = (byte)Random.Shared.Next(0x10, 0xEF);
            var reply = Exchange(deviceIndex, 0x00, 0x01, new[] { (byte)0x00, mark }, timeoutMs);
            if (reply == null)
                continue;

            if (reply.Value.IsError)
            {
                if (reply.Value.ErrorCode == 0x01)
                    return 1.0;
                return null;
            }

            if (reply.Value.Data is { Length: >= 2 })
                return reply.Value.Data[0] + reply.Value.Data[1] / 10.0;
        }
        return null;
    }

    public byte? GetFeature(int deviceIndex, ushort featureId, int timeoutMs = 900)
    {
        var reply = Exchange(deviceIndex, 0x00, 0x00,
            new[] { (byte)(featureId >> 8), (byte)featureId }, timeoutMs);
        if (reply == null || reply.Value.IsError || reply.Value.Data is not { Length: >= 1 })
            return null;
        // 特性索引 0 表示设备不支持该特性
        return reply.Value.Data[0] == 0 ? null : reply.Value.Data[0];
    }

    public HidppReply? CallFeature(int deviceIndex, byte featureIndex, byte function, ReadOnlySpan<byte> parameters,
        int timeoutMs = 900)
        => Exchange(deviceIndex, featureIndex, function, parameters, timeoutMs);

    private (byte ReportId, byte DeviceIndex, byte[] Payload)? ReadAny(int timeoutMs)
    {
        var shortReply = _short.ReadInput(timeoutMs);
        if (shortReply != null)
            return (shortReply[0], shortReply[1], shortReply[2..]);

        if (_long != null)
        {
            var longReply = _long.ReadInput(1);
            if (longReply != null)
                return (longReply[0], longReply[1], longReply[2..]);
        }

        return null;
    }

    private void Drain()
    {
        for (var i = 0; i < 24; i++)
        {
            var got = false;
            if (_short.ReadInput(1) != null) got = true;
            if (_long != null && _long.ReadInput(1) != null) got = true;
            if (!got)
                return;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _short.Dispose();
        _long?.Dispose();
    }

    private static string DescribeError(byte code) => code switch
    {
        0x01 => "INVALID_SUB_ID（该通道按 1.0 解析）",
        0x02 => "INVALID_ADDRESS",
        0x03 => "INVALID_VALUE",
        0x04 => "CONNECTION_REQUEST_FAILED（设备休眠/不可达）",
        0x05 => "TOO_MANY_DEVICES",
        0x06 => "ALREADY_EXISTS",
        0x07 => "BUSY",
        0x08 => "UNKNOWN_DEVICE（该索引没有设备）",
        0x09 => "RESOURCE_ERROR",
        0x0A => "REQUEST_UNAVAILABLE",
        0x0B => "UNSUPPORTED_PARAMETER_VALUE",
        0x0C => "INVALID_PARAMETER",
        _ => $"错误 0x{code:X2}"
    };
}

public readonly record struct HidppReply(byte[]? Data, bool IsError, byte ErrorCode, string Message)
{
    public bool Ok => !IsError && Data != null;
}
