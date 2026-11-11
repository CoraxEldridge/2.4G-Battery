using System.Drawing.Drawing2D;

namespace BatteryWidget;

/// <summary>macOS 分段控件（Segmented Control），选中块带滑动动画。</summary>
public sealed class MacSegmentedControl : Control
{
    private const int DurationMs = 180;

    private readonly Font _font = MacTheme.Font(8.5f);
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = Anim.TickMs };

    private int _selectedIndex;
    private float _position;
    private float _from;
    private float _to;
    private double _startMs;
    private double _lastFrameMs;

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
            BeginAnimation();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;

    /// <summary>每渲染一帧触发，参数为该帧时间戳（毫秒），供帧率自测使用。</summary>
    internal event Action<double>? FrameRendered;

    public MacSegmentedControl()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        Height = 28;
        Cursor = Cursors.Hand;
        BackColor = Color.Transparent;
        _animation.Tick += (_, _) => Animate();
    }

    /// <summary>从当前偏移开始补间，快速连点也顺滑。</summary>
    private void BeginAnimation()
    {
        _from = _position;
        _to = _selectedIndex;
        _startMs = Anim.NowMs;
        _lastFrameMs = 0;
        Anim.BeginFramePrecision();
        _animation.Start();
    }

    private void Animate()
    {
        var now = Anim.NowMs;
        if (now - _lastFrameMs < Anim.FrameIntervalMs)
            return;

        _lastFrameMs = now;

        var progress = (float)Math.Clamp((now - _startMs) / DurationMs, 0.0, 1.0);
        var eased = Anim.EaseOutCubic(progress);
        _position = _from + (_to - _from) * eased;

        if (progress >= 1f)
        {
            _position = _to;
            _animation.Stop();
            Anim.EndFramePrecision();
        }

        Invalidate();
        Update(); // 立即重绘，避免等待消息循环导致掉帧
        FrameRendered?.Invoke(now);
    }

    // 在 MouseDown 立即响应，避免快速点击丢失
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || Items.Length == 0)
            return;

        Focus();
        var segment = Width / (float)Items.Length;
        SelectedIndex = Math.Clamp((int)(e.X / segment), 0, Items.Length - 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = MacTheme.SmallTextHint;

        var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var bgPath = MacTheme.RoundedRect(outer, 7f))
        using (var bgBrush = new SolidBrush(Color.FromArgb(233, 233, 236)))
            g.FillPath(bgBrush, bgPath);

        if (Items.Length == 0)
            return;

        var segmentWidth = outer.Width / Items.Length;
        var position = Math.Clamp(_position, 0f, Items.Length - 1f);
        var selectedRect = new RectangleF(outer.X + segmentWidth * position + 1, outer.Y + 1,
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
            // 离选中块越近越深，滑动时文字颜色跟着过渡
            var weight = Math.Clamp(1f - Math.Abs(i - position), 0f, 1f);
            var color = Blend(MacTheme.Secondary, MacTheme.Text, weight);
            using var brush = new SolidBrush(color);
            var rect = new RectangleF(outer.X + segmentWidth * i, outer.Y, segmentWidth, outer.Height);
            g.DrawString(Items[i], _font, brush, rect, format);
        }
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)(from.R + (to.R - from.R) * amount),
        (int)(from.G + (to.G - from.G) * amount),
        (int)(from.B + (to.B - from.B) * amount));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }
}
