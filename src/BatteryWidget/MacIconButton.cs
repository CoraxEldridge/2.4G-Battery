using System.Drawing.Drawing2D;

namespace BatteryWidget;

/// <summary>标题栏里的 macOS 工具栏图标按钮（圆形 hover 背景）。</summary>
public sealed class MacIconButton : Control
{
    private bool _hover;
    private bool _pressed;

    /// <summary>图标绘制回调。</summary>
    public Action<Graphics, RectangleF, Color>? DrawIcon { get; set; }

    public MacIconButton()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(30, 30);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
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

        if (_hover || _pressed)
        {
            using var brush = new SolidBrush(Color.FromArgb(_pressed ? 34 : 20, 0, 0, 0));
            g.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
        }

        if (DrawIcon != null)
            DrawIcon(g, new RectangleF(7, 7, Width - 14, Height - 14), MacTheme.Secondary);
    }
}


