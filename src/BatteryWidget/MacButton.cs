using System.Drawing.Drawing2D;

namespace BatteryWidget;

/// <summary>macOS 风格圆角按钮。</summary>
public sealed class MacButton : Control
{
    public enum ButtonStyle
    {
        Primary,
        Secondary,
        Plain
    }

    private bool _hover;
    private bool _pressed;
    private readonly Font _font = MacTheme.Font(9f, FontStyle.Regular);

    public ButtonStyle Kind { get; set; } = ButtonStyle.Secondary;

    public MacButton()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(96, 30);
        Cursor = Cursors.Hand;
        TabStop = true;
        BackColor = Color.Transparent;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _pressed = true;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = MacTheme.RoundedRect(rect, 7f);

        Color fill;
        Color textColor;
        var border = Color.Transparent;

        switch (Kind)
        {
            case ButtonStyle.Primary:
                fill = _pressed ? Color.FromArgb(0, 100, 210) : _hover ? Color.FromArgb(26, 134, 255) : MacTheme.Blue;
                textColor = Color.White;
                break;
            case ButtonStyle.Plain:
                fill = _pressed ? Color.FromArgb(229, 229, 234) : _hover ? MacTheme.Hover : Color.Transparent;
                textColor = MacTheme.Blue;
                break;
            default:
                fill = _pressed ? Color.FromArgb(233, 233, 235) : _hover ? MacTheme.Hover : MacTheme.Card;
                textColor = MacTheme.Text;
                border = Color.Transparent;
                break;
        }

        if (fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }

        if (border.A > 0)
        {
            using var pen = new Pen(border, 1f);
            g.DrawPath(pen, path);
        }

        if (string.IsNullOrEmpty(Text))
            return;

        using var textBrush = new SolidBrush(textColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        g.DrawString(Text, _font, textBrush, new RectangleF(4, 0, Width - 8, Height), format);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _font.Dispose();
        base.Dispose(disposing);
    }
}



