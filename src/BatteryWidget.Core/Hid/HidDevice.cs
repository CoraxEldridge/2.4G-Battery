using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatteryWidget.Core.Hid;

/// <summary>可读写的 HID 设备句柄，所有 I/O 都是带超时的重叠 I/O。</summary>
public sealed class HidDevice : IDisposable
{
    private readonly SafeFileHandle _handle;
    private IntPtr _preparsedData;
    private bool _disposed;

    public HidInterface Interface { get; }

    public SafeFileHandle Handle => _handle;

    /// <summary>解析后的报告描述符（用于按值读取），Dispose 时释放。</summary>
    public IntPtr PreparsedData => _preparsedData;

    private HidDevice(HidInterface iface, SafeFileHandle handle)
    {
        Interface = iface;
        _handle = handle;
        try
        {
            if (NativeMethods.HidD_GetPreparsedData(_handle, out var preparsed))
                _preparsedData = preparsed;
        }
        catch
        {
            _preparsedData = IntPtr.Zero;
        }
        try { NativeMethods.HidD_SetNumInputBuffers(_handle, 64); } catch { }
    }

    /// <summary>打开设备。write=false 时只需要读权限。</summary>
    public static HidDevice Open(HidInterface iface, bool write)
    {
        var handle = HidEnumerator.OpenHandle(iface.Path, read: true, write: write);
        if (handle.IsInvalid)
        {
            var err = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new HidException($"打开 HID 设备失败（Win32 {err}）：{(err == 5 ? "拒绝访问，可能被 Logi Options+ 等程序独占" : new Win32Exception(err).Message)}");
        }
        return new HidDevice(iface, handle);
    }

    /// <summary>写一条 Output Report；长度不足时补 0 到接口的输出报文长度。</summary>
    public void WriteOutput(ReadOnlySpan<byte> report)
    {
        ThrowIfDisposed();
        var length = Math.Max(Interface.OutputReportLength, report.Length);
        var buffer = new byte[length];
        report.CopyTo(buffer);
        if (!WriteWithTimeout(buffer, 500, out var error))
            throw new HidException($"写 Output Report 失败（Win32 {error}）");
    }

    /// <summary>写 Output Report，失败时返回 Win32 错误码而不是抛异常。</summary>
    public bool TryWriteOutput(ReadOnlySpan<byte> report, int timeoutMs, out int error)
    {
        ThrowIfDisposed();
        var length = Math.Max(Interface.OutputReportLength, report.Length);
        var buffer = new byte[length];
        report.CopyTo(buffer);
        return WriteWithTimeout(buffer, timeoutMs, out error);
    }

    /// <summary>读取一条 Input Report；超时返回 null。</summary>
    public byte[]? ReadInput(int timeoutMs)
    {
        ThrowIfDisposed();
        if (Interface.InputReportLength <= 0)
            return null;

        var buffer = new byte[Interface.InputReportLength];
        var read = ReadWithTimeout(buffer, timeoutMs, out _);
        if (!read)
            return null;

        // Windows 下 InputReportLength 已包含 Report ID 字节，直接返回
        return buffer;
    }

    /// <summary>读取 Feature Report（返回长度 = 接口的 FeatureReportByteLength）。</summary>
    public byte[]? GetFeature(int reportId, int? length = null)
    {
        ThrowIfDisposed();
        var size = length ?? Math.Max(Interface.FeatureReportLength, 1);
        var buffer = new byte[size];
        buffer[0] = (byte)reportId;
        if (!NativeMethods.HidD_GetFeature(_handle, buffer, buffer.Length))
            return null;
        return buffer;
    }

    /// <summary>写 Feature Report。</summary>
    public bool SetFeature(ReadOnlySpan<byte> report)
    {
        ThrowIfDisposed();
        var size = Math.Max(Interface.FeatureReportLength, report.Length);
        var buffer = new byte[size];
        report.CopyTo(buffer);
        return NativeMethods.HidD_SetFeature(_handle, buffer, buffer.Length);
    }

    /// <summary>把输入缓冲区里积压的报文读干净。</summary>
    public void Drain(int maxReports = 64, int timeoutMs = 20)
    {
        for (var i = 0; i < maxReports; i++)
        {
            if (ReadInput(timeoutMs) == null)
                return;
        }
    }

    private bool WriteWithTimeout(byte[] buffer, int timeoutMs, out int error)
    {
        error = 0;
        var evt = NativeMethods.CreateEvent(IntPtr.Zero, true, false, null);
        var overlapped = new NativeMethods.OVERLAPPED { hEvent = evt };
        try
        {
            var ok = NativeMethods.WriteFile(_handle, buffer, buffer.Length, out var written, ref overlapped);
            if (!ok)
            {
                error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_IO_PENDING)
                    return false;

                var wait = NativeMethods.WaitForSingleObject(evt, (uint)timeoutMs);
                if (wait != 0)
                {
                    NativeMethods.CancelIoEx(_handle, ref overlapped);
                    error = NativeMethods.ERROR_SEM_TIMEOUT;
                    return false;
                }

                if (!NativeMethods.GetOverlappedResult(_handle, ref overlapped, out written, false))
                {
                    error = Marshal.GetLastWin32Error();
                    return false;
                }
            }
            return true;
        }
        finally
        {
            if (evt != IntPtr.Zero)
                NativeMethods.CloseHandle(evt);
        }
    }

    private bool ReadWithTimeout(byte[] buffer, int timeoutMs, out int error)
    {
        error = 0;
        var evt = NativeMethods.CreateEvent(IntPtr.Zero, true, false, null);
        var overlapped = new NativeMethods.OVERLAPPED { hEvent = evt };
        try
        {
            var ok = NativeMethods.ReadFile(_handle, buffer, buffer.Length, out var read, ref overlapped);
            if (!ok)
            {
                error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_IO_PENDING)
                    return false;

                var wait = NativeMethods.WaitForSingleObject(evt, (uint)timeoutMs);
                if (wait != 0)
                {
                    NativeMethods.CancelIoEx(_handle, ref overlapped);
                    error = NativeMethods.ERROR_SEM_TIMEOUT;
                    return false;
                }

                if (!NativeMethods.GetOverlappedResult(_handle, ref overlapped, out read, false))
                {
                    error = Marshal.GetLastWin32Error();
                    return false;
                }
            }
            return true;
        }
        finally
        {
            if (evt != IntPtr.Zero)
                NativeMethods.CloseHandle(evt);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_preparsedData != IntPtr.Zero)
        {
            try { NativeMethods.HidD_FreePreparsedData(_preparsedData); } catch { }
            _preparsedData = IntPtr.Zero;
        }
        _handle.Dispose();
    }
}

public sealed class HidException : Exception
{
    public HidException(string message) : base(message) { }
}
