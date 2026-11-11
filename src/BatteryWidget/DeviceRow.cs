using System.Drawing.Drawing2D;
using BatteryWidget.Core.Devices;

namespace BatteryWidget;

/// <summary>设备选择列表里的一行（macOS 风格复选行）。</summary>
public sealed class DeviceRow : Control
{
    private readonly Font _titleFont = MacTheme.Font(9.5f, FontStyle.Bold);
    private readonly Font _subFont = MacTheme.Font(8.25f);
    private bool _hover;
    private bool _checked;

    public DeviceKind Kind { get; set; } = DeviceKind.Unknown;
    public string Subtitle { get; set; } = "";
    public string Detail { get; set; } = "";

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public DeviceRow()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Height = 56;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
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
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = MacTheme.SmallTextHint;

        using (var bg = new SolidBrush(_hover ? MacTheme.Hover : MacTheme.Card))
        using (var path = MacTheme.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10f))
        {
            g.FillPath(bg, path);
            using var borderPen = new Pen(Color.FromArgb(237, 237, 242), 1f);
            g.DrawPath(borderPen, path);
        }

        DrawCheckbox(g, new RectangleF(14, (Height - 18) / 2f, 18, 18));
        SfIcon.Draw(g, Kind, new RectangleF(42, (Height - 20) / 2f, 20, 20), MacTheme.Secondary, 1.3f);

        using (var titleBrush = new SolidBrush(MacTheme.Text))
            g.DrawString(Text, _titleFont, titleBrush, 72, 8);

        if (!string.IsNullOrEmpty(Subtitle))
        {
            using var subBrush = new SolidBrush(MacTheme.Secondary);
            g.DrawString(Subtitle, _subFont, subBrush, 72, 29);
        }

        if (!string.IsNullOrEmpty(Detail))
        {
            using var detailBrush = new SolidBrush(MacTheme.Tertiary);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Near,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString(Detail, _subFont, detailBrush, new RectangleF(180, 29, Width - 196, 18), format);
        }
    }

    private void DrawCheckbox(Graphics g, RectangleF rect)
    {
        using var path = MacTheme.RoundedRect(rect, 4f);
        if (Checked)
        {
            using var fill = new SolidBrush(MacTheme.Blue);
            g.FillPath(fill, path);
            using var pen = new Pen(Color.White, 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLines(pen, new[]
            {
                new PointF(rect.X + 4f, rect.Y + 9.4f),
                new PointF(rect.X + 7.4f, rect.Y + 12.8f),
                new PointF(rect.X + 14f, rect.Y + 5.6f)
            });
        }
        else
        {
            using var fill = new SolidBrush(Color.White);
            g.FillPath(fill, path);
            using var pen = new Pen(MacTheme.Separator, 1f);
            g.DrawPath(pen, path);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _subFont.Dispose();
        }
        base.Dispose(disposing);
    }
}




