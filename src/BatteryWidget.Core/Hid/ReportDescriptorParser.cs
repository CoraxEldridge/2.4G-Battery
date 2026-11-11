using System.Text;

namespace BatteryWidget.Core.Hid;

public enum HidReportKind
{
    Input,
    Output,
    Feature
}

public sealed record HidReportField(
    HidReportKind Kind,
    int ReportId,
    int BitSize,
    int ReportCount,
    int UsagePage,
    int UsageMin,
    int UsageMax,
    int LogicalMin,
    int LogicalMax,
    string CollectionPath);

/// <summary>
/// 极简 HID 报告描述符解析器：只关心 main item 的字段声明，
/// 足够用来判断「有没有标准电量字段」「厂商接口用了哪些 Report ID」。
/// </summary>
public static class ReportDescriptorParser
{
    public static List<HidReportField> Parse(byte[] descriptor)
    {
        var fields = new List<HidReportField>();
        var stack = new List<(int UsagePage, int ReportSize, int ReportCount, int ReportId)>();
        var collections = new List<(int UsagePage, int Usage, int Type)>();

        int usagePage = 0, reportSize = 0, reportCount = 0, reportId = 0, logicalMin = 0, logicalMax = 0;
        var localUsages = new List<(int Page, int Usage)>();
        var i = 0;

        while (i < descriptor.Length)
        {
            var prefix = descriptor[i++];
            if (prefix == 0xFE)
            {
                // long item：跳过
                if (i + 1 >= descriptor.Length) break;
                var longSize = descriptor[i];
                i += 2 + longSize;
                continue;
            }

            var size = prefix & 0x03;
            if (size == 3) size = 4;
            var type = (prefix >> 2) & 0x03;
            var tag = (prefix >> 4) & 0x0F;

            long value = 0;
            for (var b = 0; b < size && i < descriptor.Length; b++)
                value |= (long)descriptor[i++] << (8 * b);

            if (type == 0)
            {
                // main
                switch (tag)
                {
                    case 0x8: // Input
                    case 0x9: // Output
                    case 0xB: // Feature
                    {
                        var kind = tag switch
                        {
                            0x8 => HidReportKind.Input,
                            0x9 => HidReportKind.Output,
                            _ => HidReportKind.Feature
                        };
                        var page = usagePage;
                        var min = 0;
                        var max = 0;
                        if (localUsages.Count > 0)
                        {
                            min = localUsages[0].Usage;
                            max = localUsages[0].Usage;
                            page = localUsages[0].Page;
                            if (localUsages.Count > 1)
                                max = localUsages[^1].Usage;
                        }
                        var path = string.Join(" > ", collections.Select(c => $"0x{c.UsagePage:X4}/0x{c.Usage:X4}"));
                        fields.Add(new HidReportField(kind, reportId, reportSize, reportCount, page, min, max,
                            logicalMin, logicalMax, path));
                        localUsages.Clear();
                        break;
                    }
                    case 0xA: // Collection
                        collections.Add((usagePage, (int)value, (int)value));
                        localUsages.Clear();
                        break;
                    case 0xC: // EndCollection
                        if (collections.Count > 0)
                            collections.RemoveAt(collections.Count - 1);
                        localUsages.Clear();
                        break;
                }
            }
            else if (type == 1)
            {
                // global
                switch (tag)
                {
                    case 0x0: usagePage = (int)value; break;
                    case 0x1: logicalMin = (int)(size == 4 ? (int)value : size == 2 ? (short)value : (sbyte)value); break;
                    case 0x2: logicalMax = (int)(size == 4 ? (int)value : size == 2 ? (short)value : (sbyte)value); break;
                    case 0x7: reportSize = (int)value; break;
                    case 0x8: reportId = (int)value; break;
                    case 0x9: reportCount = (int)value; break;
                    case 0xA: // Push
                        stack.Add((usagePage, reportSize, reportCount, reportId));
                        break;
                    case 0xB: // Pop
                        if (stack.Count > 0)
                        {
                            var (p, s, c, r) = stack[^1];
                            stack.RemoveAt(stack.Count - 1);
                            usagePage = p; reportSize = s; reportCount = c; reportId = r;
                        }
                        break;
                }
            }
            else if (type == 2)
            {
                // local
                switch (tag)
                {
                    case 0x0: // Usage
                        if (size == 4)
                        {
                            localUsages.Add(((int)(value >> 16) & 0xFFFF, (int)value & 0xFFFF));
                        }
                        else
                        {
                            localUsages.Add((usagePage, (int)value));
                        }
                        break;
                    case 0x1: // Usage Minimum
                        if (localUsages.Count == 0)
                            localUsages.Add((usagePage, (int)value));
                        break;
                    case 0x2: // Usage Maximum
                        localUsages.Add((usagePage, (int)value));
                        break;
                }
            }

            if (i >= descriptor.Length)
                break;
        }

        return fields;
    }

    public static string Describe(byte[] descriptor)
    {
        var fields = Parse(descriptor);
        var sb = new StringBuilder();
        foreach (var f in fields)
        {
            sb.AppendLine(
                $"{f.Kind,-7} reportId=0x{f.ReportId:X2} size={f.BitSize,-3} count={f.ReportCount,-3} " +
                $"page=0x{f.UsagePage:X4} usage=0x{f.UsageMin:X4}" +
                (f.UsageMax != f.UsageMin ? $"-0x{f.UsageMax:X4}" : "") +
                $" logical={f.LogicalMin}..{f.LogicalMax}" +
                (string.IsNullOrEmpty(f.CollectionPath) ? "" : $"  [{f.CollectionPath}]"));
        }
        return sb.ToString();
    }

    public static string ToHex(byte[] data)
    {
        var sb = new StringBuilder(data.Length * 3);
        foreach (var b in data)
            sb.Append(b.ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }
}
