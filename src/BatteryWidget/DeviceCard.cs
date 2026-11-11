using System.Drawing.Drawing2D;
using BatteryWidget.Core.Devices;

namespace BatteryWidget;

/// <summary>一张设备电量卡片（macOS 卡片：柔和投影 + SF 风格线描图标）。</summary>
public sealed class DeviceCard : Control
{
    private const int Shadow = 8;
    private const float Corner = 16f;

    private readonly Font _nameFont = MacTheme.Font(12.5f, FontStyle.Bold);
    private readonly Font _metaFont = MacTheme.Font(8.75f);
    private readonly Font _percentFont = MacTheme.Font(25f, FontStyle.Bold);
    private readonly Font _smallFont = MacTheme.Font(8.25f);

    private DeviceSnapshot? _snapshot;
    private BatteryReading? _lastReading;
    private DateTime? _lastAt;

    public DeviceKind Kind { get; set; } = DeviceKind.Unknown;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string FallbackName { get; set; } = "无线设备";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string FallbackKind { get; set; } = "2.4G 设备";

    public DeviceCard()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint, true);
        BackColor = MacTheme.Window;
        Height = 158;
    }

    public void Apply(DeviceSnapshot? snapshot, BatteryReading? lastReading, DateTime? lastAt)
    {
        _snapshot = snapshot;
        _lastReading = lastReading;
        _lastAt = lastAt;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new RectangleF(Shadow, Shadow, Width - Shadow * 2, Height - Shadow * 2);
        using (var cardPath = MacTheme.RoundedRect(rect, Corner))
using (var cardBrush = new SolidBrush(MacTheme.Card))
    g.FillPath(cardBrush, cardPath);

        var name = _snapshot?.Name ?? FallbackName;
        var kind = _snapshot?.KindText ?? FallbackKind;
        var reading = _snapshot?.Battery ?? _lastReading;
        var percent = reading?.Percent;
        var online = _snapshot?.Online ?? false;

        // 卡片内的小字统一用灰度抗锯齿（11px 左右的汉字用 ClearType 会出现 1px 硬边）
        g.TextRenderingHint = MacTheme.SmallTextHint;

        // SF 风格线描图标
        SfIcon.Draw(g, Kind, new RectangleF(rect.X + 18, rect.Y + 17, 36, 36), MacTheme.Secondary, 1.6f);

        // 设备名单独用灰度抗锯齿，其余文字保持 ClearType
        var previousHint = g.TextRenderingHint;
        g.TextRenderingHint = MacTheme.SmallTextHint;
        using (var nameBrush = new SolidBrush(MacTheme.Text))
            g.DrawString(name, _nameFont, nameBrush, rect.X + 66, rect.Y + 16);
        g.TextRenderingHint = previousHint;

        var subtitle = _snapshot == null ? "未检测到设备" : $"{kind} · {_snapshot.Transport}";
        using (var metaBrush = new SolidBrush(MacTheme.Secondary))
            g.DrawString(subtitle, _metaFont, metaBrush, rect.X + 66, rect.Y + 39);

        using (var dotBrush = new SolidBrush(online ? MacTheme.Green : MacTheme.Orange))
            g.FillEllipse(dotBrush, rect.X + 66, rect.Y + 67, 6f, 6f);
        var stateText = online ? "在线" : _snapshot?.Error != null ? "休眠 / 不可达" : "等待读取";
        using (var stateBrush = new SolidBrush(MacTheme.Tertiary))
            g.DrawString(stateText, _smallFont, stateBrush, rect.X + 76, rect.Y + 62);

        var percentText = percent.HasValue ? $"{percent.Value}%" : "--";
        // 25pt 大字用次像素抗锯齿更锐利
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using (var percentBrush = new SolidBrush(MacTheme.LevelColor(percent)))
        {
            var size = g.MeasureString(percentText, _percentFont);
            g.DrawString(percentText, _percentFont, percentBrush, rect.Right - 18 - size.Width, rect.Y + 12);
        }

        // 回到灰度抗锯齿，后面的状态/更新时间也是小字
        g.TextRenderingHint = MacTheme.SmallTextHint;

        var statusText = online ? reading?.StatusText ?? "状态未知" : reading != null ? "休眠中" : "";
        if (!string.IsNullOrEmpty(statusText))
        {
            using var statusBrush = new SolidBrush(online ? MacTheme.Secondary : MacTheme.Tertiary);
            var size = g.MeasureString(statusText, _metaFont);
            g.DrawString(statusText, _metaFont, statusBrush, rect.Right - 18 - size.Width, rect.Y + 52);
        }

        var barRect = new RectangleF(rect.X + 66, rect.Bottom - 30, Math.Max(60, rect.Width - 84), 6);
        using (var trackPath = MacTheme.RoundedRect(barRect, 3))
        using (var trackBrush = new SolidBrush(Color.FromArgb(56, 120, 130, 150)))
            g.FillPath(trackBrush, trackPath);

        if (percent is > 0)
        {
            var fillWidth = Math.Max(6f, barRect.Width * percent.Value / 100f);
            var fillRect = new RectangleF(barRect.X, barRect.Y, fillWidth, barRect.Height);
            using var fillPath = MacTheme.RoundedRect(fillRect, 3);
            using var fillBrush = new SolidBrush(MacTheme.LevelColor(percent));
            g.FillPath(fillBrush, fillPath);
        }

        string note;
        if (online)
            note = _lastAt.HasValue ? $"更新于 {_lastAt.Value:HH:mm:ss}" : _snapshot?.Detail ?? "";
        else if (reading != null && _lastAt.HasValue)
            note = $"更新于 {_lastAt.Value:HH:mm:ss} · 轻触设备可唤醒";
        else
            note = _snapshot?.Error ?? "尚未读取到数据";

        if (!string.IsNullOrEmpty(note))
        {
            using var noteBrush = new SolidBrush(MacTheme.Tertiary);
            var clipped = note.Length > 64 ? note[..64] + "…" : note;
            g.DrawString(clipped, _smallFont, noteBrush, rect.X + 66, rect.Bottom - 20);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nameFont.Dispose();
            _metaFont.Dispose();
            _percentFont.Dispose();
            _smallFont.Dispose();
        }
        base.Dispose(disposing);
    }
}


