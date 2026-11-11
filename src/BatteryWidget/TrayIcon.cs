using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace BatteryWidget;

/// <summary>托盘图标：运行时生成 macOS 菜单栏风格的电量图标。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private Icon? _currentIcon;
    private int _lastLevel = -1;
    private bool _lastCharging;

    public event Action? ShowRequested;
    public event Action? RefreshRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add("显示主窗口", null, (_, _) => ShowRequested?.Invoke());
        _menu.Items.Add("立即刷新", null, (_, _) => RefreshRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());

        _currentIcon = CreateIcon(-1, false);
        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "2.4G Battery",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    public void UpdateTooltip(string text)
    {
        if (text.Length > 63)
            text = text[..62] + "…";
        _notifyIcon.Text = text;
    }

    public void UpdateIcon(int? level, bool charging = false)
    {
        var value = level ?? -1;
        if (value == _lastLevel && charging == _lastCharging)
            return;

        _lastLevel = value;
        _lastCharging = charging;

        var icon = CreateIcon(value, charging);
        _notifyIcon.Icon = icon;
        _currentIcon?.Dispose();
        _currentIcon = icon;
    }

    /// <summary>弹出 Windows 系统通知（通过托盘气泡，Win10/11 会进入通知中心）。</summary>
    public void Notify(string title, string text, ToolTipIcon icon = ToolTipIcon.Warning)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(8000);
    }

    /// <summary>生成预览用的图标（调试/截图用）。</summary>
    public static Icon CreateForPreview(int? level, bool charging) => CreateIcon(level ?? -1, charging);

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>macOS 菜单栏风格：圆角电池轮廓 + 右侧电极 + 按电量填充。</summary>
    private static Icon CreateIcon(int level, bool charging)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var dark = IsSystemDark();
            var ink = dark ? Color.White : Color.FromArgb(38, 38, 42);
            var low = level >= 0 && level < 20;

            var fillColor = charging
                ? Color.FromArgb(52, 199, 89)
                : low
                    ? Color.FromArgb(255, 59, 48)
                    : ink;

            // 电池外壳
            using (var shell = MacTheme.RoundedRect(new RectangleF(1.6f, 7.6f, 25.6f, 16.8f), 5.2f))
            using (var pen = new Pen(ink, 2.5f) { LineJoin = LineJoin.Round })
                g.DrawPath(pen, shell);

            // 右侧电极
            using (var nub = MacTheme.RoundedRect(new RectangleF(27.9f, 12.8f, 3.4f, 6.4f), 1.6f))
            using (var brush = new SolidBrush(ink))
                g.FillPath(brush, nub);

            // 电量填充
            if (level > 0)
            {
                var maxWidth = 20.2f;
                var width = Math.Max(2.4f, maxWidth * Math.Clamp(level, 0, 100) / 100f);
                using var fillPath = MacTheme.RoundedRect(new RectangleF(4.5f, 10.6f, width, 10.8f), 2.6f);
                using var fillBrush = new SolidBrush(fillColor);
                g.FillPath(fillBrush, fillPath);
            }

            // 充电时的闪电
            if (charging)
            {
                using var bolt = BuildBolt();
                using var boltBrush = new SolidBrush(dark ? Color.White : Color.FromArgb(16, 70, 36));
                g.FillPath(boltBrush, bolt);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath BuildBolt()
    {
        var points = new[]
        {
            new PointF(16.6f, 8.8f),
            new PointF(11.0f, 17.4f),
            new PointF(14.2f, 17.4f),
            new PointF(12.8f, 24.8f),
            new PointF(18.6f, 15.8f),
            new PointF(15.4f, 15.8f)
        };
        var path = new GraphicsPath();
        path.AddPolygon(points);
        return path;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _currentIcon?.Dispose();
    }
}


