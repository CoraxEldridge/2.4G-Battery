using System.Drawing.Drawing2D;

namespace BatteryWidget;

/// <summary>macOS 分段控件（Segmented Control）。</summary>
public sealed class MacSegmentedControl : Control
{
    private readonly Font _font = MacTheme.Font(8.5f);
    private int _selectedIndex;

    public string[] Items { get; set; } = Array.Empty<string>();

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, Math.Max(0, Items.Length - 1));
            if (clamped == _selectedIndex)
                return;
            _selectedIndex = clamped;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;

    public MacSegmentedControl()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Height = 28;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (Items.Length == 0)
            return;

        var segment = Width / (float)Items.Length;
        var index = (int)(PointToClient(Cursor.Position).X / segment);
        SelectedIndex = Math.Clamp(index, 0, Items.Length - 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var bgPath = MacTheme.RoundedRect(outer, 7f))
        using (var bgBrush = new SolidBrush(Color.FromArgb(233, 233, 236)))
            g.FillPath(bgBrush, bgPath);

        if (Items.Length == 0)
            return;

        var segmentWidth = outer.Width / Items.Length;
        var selectedRect = new RectangleF(outer.X + segmentWidth * _selectedIndex + 1, outer.Y + 1,
            segmentWidth - 2, outer.Height - 2);

        using (var shadow = new SolidBrush(Color.FromArgb(22, 0, 0, 0)))
        using (var shadowPath = MacTheme.RoundedRect(new RectangleF(selectedRect.X, selectedRect.Y + 1,
                   selectedRect.Width, selectedRect.Height), 6f))
            g.FillPath(shadow, shadowPath);

        using (var selPath = MacTheme.RoundedRect(selectedRect, 6f))
        using (var selBrush = new SolidBrush(Color.White))
            g.FillPath(selBrush, selPath);

        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        for (var i = 0; i < Items.Length; i++)
        {
            var color = i == _selectedIndex ? MacTheme.Text : MacTheme.Secondary;
            using var brush = new SolidBrush(color);
            var rect = new RectangleF(outer.X + segmentWidth * i, outer.Y, segmentWidth, outer.Height);
            g.DrawString(Items[i], _font, brush, rect, format);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _font.Dispose();
        base.Dispose(disposing);
    }
}




