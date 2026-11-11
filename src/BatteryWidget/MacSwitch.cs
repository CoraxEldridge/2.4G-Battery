using System.Drawing.Drawing2D;

namespace BatteryWidget;

/// <summary>macOS 风格开关（40×22 圆角轨道 + 带动画的白色滑块）。</summary>
public sealed class MacSwitch : Control
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };
    private bool _checked;
    private bool _hover;
    private float _position;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;
            _checked = value;
            StartAnimation();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public MacSwitch()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(40, 22);
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        _animation.Tick += (_, _) => Animate();
    }

    private void StartAnimation()
    {
        _animation.Start();
    }

    private void Animate()
    {
        var target = _checked ? 1f : 0f;
        var delta = target - _position;
        if (Math.Abs(delta) < 0.03f)
        {
            _position = target;
            _animation.Stop();
        }
        else
        {
            _position += delta * 0.55f;
        }
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new RectangleF(0.5f, 1f, Width - 1f, Height - 2f);
                var trackColor = Color.FromArgb(
            (int)(233 + (52 - 233) * _position),
            (int)(233 + (199 - 233) * _position),
            (int)(236 + (89 - 236) * _position));
        if (_hover)
            trackColor = ControlPaint.Light(trackColor, 0.08f);

        using (var path = MacTheme.RoundedRect(track, track.Height / 2f))
        using (var brush = new SolidBrush(trackColor))
            g.FillPath(brush, path);

        var knobSize = track.Height - 4;
        var travel = track.Width - knobSize - 4;
        var knobX = track.X + 2 + travel * _position;
        var knob = new RectangleF(knobX, track.Y + 2, knobSize, knobSize);

        using (var shadow = new SolidBrush(Color.FromArgb(36, 0, 0, 0)))
            g.FillEllipse(shadow, knob.X, knob.Y + 0.8f, knob.Width, knob.Height);
        using (var knobBrush = new SolidBrush(Color.White))
            g.FillEllipse(knobBrush, knob);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _animation.Dispose();
        base.Dispose(disposing);
    }
}





