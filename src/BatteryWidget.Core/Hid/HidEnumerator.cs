using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatteryWidget.Core.Hid;

/// <summary>枚举系统中所有 HID 接口，并读取接口能力（usage page、报文长度、字符串、报告描述符）。</summary>
public static class HidEnumerator
{
    public static List<HidInterface> Enumerate(bool includeDescriptor = true)
    {
        var result = new List<HidInterface>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);

        var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            return result;

        try
        {
            var ifaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>()
            };

            for (uint index = 0; ; index++)
            {
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref ifaceData))
                    break;

                var path = GetDevicePath(deviceInfoSet, ref ifaceData);
                if (string.IsNullOrEmpty(path))
                    continue;

                try
                {
                    var iface = ReadInterface(path, includeDescriptor);
                    if (iface != null)
                        result.Add(iface);
                }
                catch
                {
                    // 单个接口失败不影响整体枚举
                }
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return result;
    }

    /// <summary>打开一个 HID 接口读取能力信息。读取失败返回 null。</summary>
    public static HidInterface? ReadInterface(string path, bool includeDescriptor = true)
    {
        // 读报告描述符需要 GENERIC_READ；没有权限时退化为 0 访问（仍可读属性）
        var handle = OpenHandle(path, read: true, write: false);
        if (handle.IsInvalid)
            handle = OpenHandle(path, read: false, write: false);
        if (handle.IsInvalid)
            return null;

        using var _ = handle;

        var attributes = new NativeMethods.HIDD_ATTRIBUTES
        {
            Size = Marshal.SizeOf<NativeMethods.HIDD_ATTRIBUTES>()
        };
        if (!NativeMethods.HidD_GetAttributes(handle, ref attributes))
            return null;

        if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed))
            return null;

        NativeMethods.HIDP_CAPS caps;
        try
        {
            if (NativeMethods.HidP_GetCaps(preparsed, out caps) != NativeMethods.HIDP_STATUS_SUCCESS)
                return null;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsed);
        }

        byte[]? descriptor = null;
        if (includeDescriptor)
            descriptor = TryGetReportDescriptor(handle);

        return new HidInterface
        {
            Path = path,
            VendorId = attributes.VendorID,
            ProductId = attributes.ProductID,
            VersionNumber = attributes.VersionNumber,
            UsagePage = caps.UsagePage,
            Usage = caps.Usage,
            InputReportLength = caps.InputReportByteLength,
            OutputReportLength = caps.OutputReportByteLength,
            FeatureReportLength = caps.FeatureReportByteLength,
            Product = GetString(handle, NativeMethods.HidD_GetProductString),
            Manufacturer = GetString(handle, NativeMethods.HidD_GetManufacturerString),
            Serial = GetString(handle, NativeMethods.HidD_GetSerialNumberString),
            ReportDescriptor = descriptor,
            InstanceSuffix = ExtractInstanceSuffix(path)
        };
    }

    internal static SafeFileHandle OpenHandle(string path, bool read, bool write)
    {
        uint access = 0;
        if (read) access |= NativeMethods.GENERIC_READ;
        if (write) access |= NativeMethods.GENERIC_WRITE;

        return NativeMethods.CreateFile(path, access,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
            IntPtr.Zero, NativeMethods.OPEN_EXISTING, NativeMethods.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
    }

    private static string GetDevicePath(IntPtr deviceInfoSet, ref NativeMethods.SP_DEVICE_INTERFACE_DATA ifaceData)
    {
        const int bufferSize = 1024;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize：x64 为 8，x86 为 6
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref ifaceData, buffer, bufferSize,
                    out _, IntPtr.Zero))
                return "";
            return Marshal.PtrToStringUni(buffer + 4) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string GetString(SafeFileHandle handle, Func<SafeFileHandle, char[], uint, bool> getter)
    {
        var buffer = new char[256];
        return getter(handle, buffer, (uint)buffer.Length) ? new string(buffer).TrimEnd('\0') : "";
    }

    private static byte[]? TryGetReportDescriptor(SafeFileHandle handle)
    {
        try
        {
            var buffer = new byte[4096];
            uint length = (uint)buffer.Length;
            if (!NativeMethods.HidD_GetReportDescriptor(handle, buffer, ref length) || length == 0 || length > buffer.Length)
                return null;
            var result = new byte[length];
            Array.Copy(buffer, result, (int)length);
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractInstanceSuffix(string path)
    {
        var parts = path.Split('#');
        return parts.Length >= 2 ? parts[1] : "";
    }
}
