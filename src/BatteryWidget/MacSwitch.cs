using System.Drawing.Drawing2D;

namespace BatteryWidget;

/// <summary>macOS 风格开关（40×22 圆角轨道 + 带动画的白色滑块）。</summary>
public sealed class MacSwitch : Control
{
    private const int DurationMs = 180;

    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 1000 / 60 };
    private bool _checked;
    private bool _hover;
    private float _position;
    private float _from;
    private float _to;
    private long _startTicks;

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
                return;

            _checked = value;
            BeginAnimation();
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

    /// <summary>从当前位置开始补间，快速连点也不会跳变。</summary>
    private void BeginAnimation()
    {
        _from = _position;
        _to = _checked ? 1f : 0f;
        _startTicks = Environment.TickCount64;
        _animation.Start();
    }

    private void Animate()
    {
        var progress = Math.Clamp((Environment.TickCount64 - _startTicks) / (float)DurationMs, 0f, 1f);
        var eased = 1f - MathF.Pow(1f - progress, 3f);
        _position = _from + (_to - _from) * eased;

        if (progress >= 1f)
        {
            _position = _to;
            _animation.Stop();
        }

        Invalidate();
    }

    // 在 MouseDown 立即切换：比 Click（要求按下与抬起都在控件内）更跟手
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            Checked = !Checked;
        }
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
