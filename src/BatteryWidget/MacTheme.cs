using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BatteryWidget;

/// <summary>macOS 浅色风格配色与绘制工具。</summary>
internal static class MacTheme
{
    public static readonly Color Window = Color.FromArgb(242, 242, 247);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(229, 229, 234);
    public static readonly Color Separator = Color.FromArgb(209, 209, 214);
    public static readonly Color Text = Color.FromArgb(29, 29, 31);
    public static readonly Color Secondary = Color.FromArgb(110, 110, 115);
    public static readonly Color Tertiary = Color.FromArgb(142, 142, 147);
    public static readonly Color Blue = Color.FromArgb(0, 122, 255);
    public static readonly Color Green = Color.FromArgb(52, 199, 89);
    public static readonly Color Orange = Color.FromArgb(255, 159, 10);
    public static readonly Color Red = Color.FromArgb(255, 59, 48);
    public static readonly Color Track = Color.FromArgb(233, 233, 235);
    public static readonly Color Hover = Color.FromArgb(245, 245, 247);
    public static readonly Color Control = Color.FromArgb(242, 242, 247);
    public static readonly Color Chip = Color.FromArgb(242, 242, 247);

    public static readonly Color CloseLight = Color.FromArgb(255, 95, 87);
    public static readonly Color MinimizeLight = Color.FromArgb(254, 188, 46);
    public static readonly Color ZoomLight = Color.FromArgb(40, 200, 64);

    private static readonly PrivateFontCollection? RegularFonts;
    private static readonly PrivateFontCollection? SemiboldFonts;
    private static readonly FontFamily? RegularFamily;
    private static readonly FontFamily? SemiboldFamily;
    private static readonly string FallbackFamily;

    /// <summary>当前实际使用的字体家族名（诊断用）。</summary>
    public static string FontInfo =>
        (RegularFamily?.Name ?? FallbackFamily) +
        (SemiboldFamily != null ? " / " + SemiboldFamily.Name : "（无粗体）");

    static MacTheme()
    {
        RegularFonts = LoadEmbedded("BatteryWidget.Fonts.PingFangSC-Regular.ttf");
        SemiboldFonts = LoadEmbedded("BatteryWidget.Fonts.PingFangSC-Semibold.ttf");
        RegularFamily = FirstFamily(RegularFonts);
        SemiboldFamily = FirstFamily(SemiboldFonts);
        FallbackFamily = ResolveFamily();
    }

    private static PrivateFontCollection? LoadEmbedded(string resourceName)
    {
        try
        {
            using var stream = typeof(MacTheme).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                return null;

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);

            // GDI+ 要求这块内存在字体集合生命周期内一直有效，故意不释放
            var memory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, memory, bytes.Length);

            var collection = new PrivateFontCollection();
            collection.AddMemoryFont(memory, bytes.Length);
            return collection;
        }
        catch
        {
            return null;
        }
    }

    private static FontFamily? FirstFamily(PrivateFontCollection? collection)
        => collection is { Families.Length: > 0 } ? collection.Families[0] : null;

    private static string ResolveFamily()
    {
        try
        {
            using var installed = new InstalledFontCollection();
            var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in new[] { "PingFang SC", "PingFang HK", "苹方-简", "苹方", "MiSans", "HarmonyOS Sans SC", "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI" })
            {
                if (names.Contains(candidate))
                    return candidate;
            }
        }
        catch
        {
            // 字体枚举失败时用默认
        }
        return "Segoe UI";
    }

    public static Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        // 粗体优先用内嵌的 Semibold 字面，常规用 Regular
        var family = (style & FontStyle.Bold) != 0 && SemiboldFamily != null
            ? SemiboldFamily
            : RegularFamily;

        if (family != null)
            return new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);

        return new Font(FallbackFamily, size, style, GraphicsUnit.Point);
    }

    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color LevelColor(int? percent) => percent switch
    {
        null => Tertiary,
        < 20 => Red,
        < 50 => Orange,
        _ => Green
    };

    public static void DrawCard(Graphics g, RectangleF rect, Color fill, Color border, float radius)
    {
        using var path = RoundedRect(rect, radius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);
        using var pen = new Pen(border, 1f);
        g.DrawPath(pen, path);
    }

    /// <summary>macOS 卡片的柔和投影。</summary>
    public static void DrawSoftShadow(Graphics g, RectangleF rect, float radius, int layers = 4, int maxAlpha = 18)
    {
        for (var i = layers; i >= 1; i--)
        {
            var spread = i * 1.6f;
            var shadowRect = new RectangleF(rect.X - spread, rect.Y - spread + 1.8f,
                rect.Width + spread * 2, rect.Height + spread * 2);
            using var path = RoundedRect(shadowRect, radius + spread);
            using var brush = new SolidBrush(Color.FromArgb(Math.Max(3, maxAlpha / i), 0, 0, 0));
            g.FillPath(brush, path);
        }
    }
}



