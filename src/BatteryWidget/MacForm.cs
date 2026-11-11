using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BatteryWidget;

/// <summary>
/// macOS 风格无边框窗口：左侧红黄绿交通灯、可拖动标题栏、边缘拖拽改大小、
/// Win11 圆角 + 投影。
/// </summary>
public class MacForm : Form
{
    private readonly TitleBar _titleBar;
    private readonly Panel _body;
    private bool _forceClose;

    /// <summary>点红灯是关闭还是隐藏到托盘。</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>标题栏高度。</summary>
    public int TitleBarHeight => _titleBar.Height;

    public Panel Body => _body;

    public MacForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        BackColor = MacTheme.Window;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);

        _titleBar = new TitleBar(this) { Dock = DockStyle.Top, Height = 52 };

        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            // 取不到图标不影响运行
        }
        _body = new Panel { Dock = DockStyle.Fill, BackColor = MacTheme.Window };

        Controls.Add(_body);
        Controls.Add(_titleBar);
    }

    /// <summary>往标题栏右侧加一个工具栏按钮（macOS 风格）。</summary>
    public void AddTitleBarControl(Control control)
    {
        control.BackColor = MacTheme.Window;
        _titleBar.Controls.Add(control);
        _titleBar.PerformToolbarLayout();
    }

    /// <summary>真正关闭（不走“收进托盘”分支）。</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    public void ToggleZoom()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryEnableRoundedCorners();
    }

    private void TryEnableRoundedCorners()
    {
        try
        {
            // Windows 11：DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2
            var preference = 2;
            DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
        }
        catch
        {
            // 旧系统忽略
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (CloseToTray && !_forceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if (m.Result == (IntPtr)1) // HTCLIENT：再判断是否在边缘
            {
                var screen = new Point((short)(long)(m.LParam.ToInt64() & 0xFFFF),
                    (short)((long)(m.LParam.ToInt64() >> 16) & 0xFFFF));
                var p = PointToClient(screen);
                const int grip = 6;
                var left = p.X <= grip;
                var right = p.X >= ClientSize.Width - grip;
                var top = p.Y <= grip;
                var bottom = p.Y >= ClientSize.Height - grip;

                m.Result = (IntPtr)((left && top) ? 13
                    : (right && top) ? 14
                    : (left && bottom) ? 16
                    : (right && bottom) ? 17
                    : left ? 10
                    : right ? 11
                    : top ? 12
                    : bottom ? 15
                    : 1);
            }
            return;
        }

        base.WndProc(ref m);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>标题栏：三个交通灯 + 居中标题 + 拖动。</summary>
    private sealed class TitleBar : Control
    {
        private readonly MacForm _owner;
        private int _hoverLight = -1;
        private int _pressedLight = -1;
        private readonly Font _titleFont = MacTheme.Font(9.75f, FontStyle.Bold);

        private static readonly int[] CenterX = { 22, 42, 62 };
        private const int Radius = 6;

        private int CenterY => Height / 2;
        private Point CenterOf(int index) => new(CenterX[index], CenterY);

        public TitleBar(MacForm owner)
        {
            _owner = owner;
            DoubleBuffered = true;
            BackColor = MacTheme.Window;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        public void PerformToolbarLayout()
        {
            var x = Width - 10;
            foreach (Control control in Controls)
            {
                x -= control.Width;
                control.Location = new Point(x, (Height - control.Height) / 2);
                x -= 2;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PerformToolbarLayout();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            PerformToolbarLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

            for (var i = 0; i < CenterX.Length; i++)
                DrawLight(g, i);

            var title = _owner.Text;
            if (!string.IsNullOrEmpty(title))
            {
                using var brush = new SolidBrush(MacTheme.Secondary);
                var size = g.MeasureString(title, _titleFont);
                g.DrawString(title, _titleFont, brush, (Width - size.Width) / 2f, (Height - size.Height) / 2f);
            }
        }

        private void DrawLight(Graphics g, int index)
        {
            var color = index switch
            {
                0 => MacTheme.CloseLight,
                1 => MacTheme.MinimizeLight,
                _ => MacTheme.ZoomLight
            };
            var center = CenterOf(index);
            var rect = new RectangleF(center.X - Radius, center.Y - Radius, Radius * 2, Radius * 2);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, rect);

            if (_hoverLight != index)
                return;

            using var pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1.4f);
            var cx = center.X;
            var cy = center.Y;
            switch (index)
            {
                case 0:
                    g.DrawLine(pen, cx - 2.6f, cy - 2.6f, cx + 2.6f, cy + 2.6f);
                    g.DrawLine(pen, cx - 2.6f, cy + 2.6f, cx + 2.6f, cy - 2.6f);
                    break;
                case 1:
                    g.DrawLine(pen, cx - 3f, cy, cx + 3f, cy);
                    break;
                default:
                    g.DrawLine(pen, cx - 2.6f, cy, cx + 2.6f, cy);
                    g.DrawLine(pen, cx, cy - 2.6f, cx, cy + 2.6f);
                    break;
            }
        }

        private int HitTest(Point p)
        {
            for (var i = 0; i < CenterX.Length; i++)
            {
                var center = CenterOf(i);
                var dx = p.X - center.X;
                var dy = p.Y - center.Y;
                if (dx * dx + dy * dy <= (Radius + 2) * (Radius + 2))
                    return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var hit = HitTest(e.Location);
            if (hit != _hoverLight)
            {
                _hoverLight = hit;
                Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverLight != -1)
            {
                _hoverLight = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var hit = HitTest(e.Location);
            if (hit >= 0)
            {
                _pressedLight = hit;
                return;
            }

            ReleaseCapture();
            SendMessage(_owner.Handle, 0x00A1 /*WM_NCLBUTTONDOWN*/, (IntPtr)2 /*HTCAPTION*/, IntPtr.Zero);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_pressedLight < 0)
                return;

            var hit = HitTest(e.Location);
            var light = _pressedLight;
            _pressedLight = -1;
            if (hit != light)
                return;

            switch (light)
            {
                case 0:
                    _owner.Close();
                    break;
                case 1:
                    _owner.WindowState = FormWindowState.Minimized;
                    break;
                default:
                    _owner.ToggleZoom();
                    break;
            }
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            if (HitTest(PointToClient(Cursor.Position)) < 0)
                _owner.ToggleZoom();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _titleFont.Dispose();
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}

