using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatteryWidget.Core.Hid;

/// <summary>HID 值字段（Value Cap）读取，用来找标准电量字段。</summary>
internal static class HidValueCaps
{
    public const int ReportTypeInput = 0;
    public const int ReportTypeOutput = 1;
    public const int ReportTypeFeature = 2;

    private const int HidpStatusSuccess = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Range
    {
        public ushort UsageMin, UsageMax;
        public ushort StringMin, StringMax;
        public ushort DesignatorMin, DesignatorMax;
        public ushort DataIndexMin, DataIndexMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotRange
    {
        public ushort Usage, Reserved1;
        public ushort StringIndex, Reserved2;
        public ushort DesignatorIndex, Reserved3;
        public ushort DataIndex, Reserved4;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct UsageUnion
    {
        [FieldOffset(0)] public Range Range;
        [FieldOffset(0)] public NotRange NotRange;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ValueCaps
    {
        public ushort UsagePage;
        public byte ReportId;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] Reserved2;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        public UsageUnion Union;
    }

    [DllImport("hid.dll")]
    private static extern int HidP_GetValueCaps(int reportType, [Out] ValueCaps[] valueCaps,
        ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection,
        ushort usage, out uint usageValue, IntPtr preparsedData, byte[] report, uint reportLength);

    public sealed record Cap(ushort UsagePage, ushort Usage, byte ReportId, int LogicalMax, int BitSize);

    public static List<Cap> GetFeatureCaps(SafeFileHandle device, IntPtr preparsed)
    {
        var result = new List<Cap>();
        const int maxCaps = 64;
        var buffer = new ValueCaps[maxCaps];
        ushort length = maxCaps;
        var status = HidP_GetValueCaps(ReportTypeFeature, buffer, ref length, preparsed);
        if (status != HidpStatusSuccess)
            return result;

        for (var i = 0; i < length && i < buffer.Length; i++)
        {
            var caps = buffer[i];
            if (caps.ReportCount == 0)
                continue;

            if (caps.IsRange != 0)
            {
                var min = caps.Union.Range.UsageMin;
                var max = caps.Union.Range.UsageMax;
                for (var usage = min; usage <= max && usage - min < 8; usage++)
                    result.Add(new Cap(caps.UsagePage, usage, caps.ReportId, caps.LogicalMax, caps.BitSize));
            }
            else
            {
                result.Add(new Cap(caps.UsagePage, caps.Union.NotRange.Usage, caps.ReportId, caps.LogicalMax, caps.BitSize));
            }
        }

        return result;
    }

    public static uint? GetFeatureValue(SafeFileHandle device, IntPtr preparsed, byte[] report,
        ushort usagePage, ushort usage, int reportLength)
    {
        var status = HidP_GetUsageValue(ReportTypeFeature, usagePage, 0, usage, out var value, preparsed,
            report, (uint)reportLength);
        return status == HidpStatusSuccess ? value : null;
    }
}
