using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BatteryWidget;

/// <summary>用灰度抗锯齿（AntiAliasGridFit）绘制文字的标签，用于大字号标题。</summary>
public sealed class GrayscaleLabel : Control
{
    private static readonly StringFormat Format = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
    };

    public GrayscaleLabel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }

    /// <summary>按文字实际大小调整控件尺寸。</summary>
    public void FitToText()
    {
        using var g = CreateGraphics();
        var size = g.MeasureString(Text, Font, PointF.Empty, Format);
        Size = new Size((int)Math.Ceiling(size.Width) + 2, (int)Math.Ceiling(size.Height) + 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var brush = new SolidBrush(ForeColor);
        g.DrawString(Text, Font, brush, 0f, 0f, Format);
    }
}
