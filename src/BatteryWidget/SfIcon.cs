using System.Drawing.Drawing2D;
using BatteryWidget.Core.Devices;

namespace BatteryWidget;

/// <summary>SF Symbols 风格的线描图标（自绘，避免使用 Windows 字体图标）。</summary>
internal static class SfIcon
{
    public static void Draw(Graphics g, DeviceKind kind, RectangleF rect, Color color, float weight = 1.5f)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, weight)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        switch (kind)
        {
            case DeviceKind.Keyboard:
                DrawKeyboard(g, pen, rect);
                break;
            case DeviceKind.Touchpad:
                DrawTouchpad(g, pen, rect);
                break;
            case DeviceKind.Mouse:
                DrawMouse(g, pen, rect);
                break;
            default:
                DrawGeneric(g, pen, rect);
                break;
        }

        g.Restore(state);
    }

    private static void DrawKeyboard(Graphics g, Pen pen, RectangleF outer)
    {
        var w = outer.Width * 0.92f;
        var h = w * 0.62f;
        var body = new RectangleF(outer.X + (outer.Width - w) / 2f, outer.Y + (outer.Height - h) / 2f, w, h);
        using var path = MacTheme.RoundedRect(body, h * 0.18f);
        g.DrawPath(pen, path);

        var dot = Math.Max(1.4f, w * 0.055f);
        var gap = (body.Width - dot * 4) / 5f;
        for (var row = 0; row < 2; row++)
        {
            var y = body.Y + h * 0.3f + row * h * 0.22f;
            for (var i = 0; i < 4; i++)
            {
                var x = body.X + gap + i * (dot + gap);
                g.FillRectangle(new SolidBrush(pen.Color), x, y, dot, dot);
            }
        }

        var barY = body.Y + h * 0.72f;
        g.DrawLine(pen, body.X + body.Width * 0.3f, barY, body.Right - body.Width * 0.3f, barY);
    }

    private static void DrawTouchpad(Graphics g, Pen pen, RectangleF outer)
    {
        var w = outer.Width * 0.86f;
        var h = w * 0.74f;
        var body = new RectangleF(outer.X + (outer.Width - w) / 2f, outer.Y + (outer.Height - h) / 2f + 1, w, h);
        using var path = MacTheme.RoundedRect(body, h * 0.16f);
        g.DrawPath(pen, path);

        var r = w * 0.11f;
        g.DrawEllipse(pen, body.X + body.Width / 2f - r, body.Bottom - h * 0.34f - r, r * 2, r * 2);
    }

    private static void DrawMouse(Graphics g, Pen pen, RectangleF outer)
    {
        var w = outer.Width * 0.56f;
        var h = outer.Height * 0.86f;
        var body = new RectangleF(outer.X + (outer.Width - w) / 2f, outer.Y + (outer.Height - h) / 2f, w, h);
        using var path = MacTheme.RoundedRect(body, w / 2f);
        g.DrawPath(pen, path);
        g.DrawLine(pen, body.X + body.Width / 2f, body.Y + 1.5f, body.X + body.Width / 2f, body.Y + h * 0.34f);
    }

    private static void DrawGeneric(Graphics g, Pen pen, RectangleF outer)
    {
        var w = outer.Width * 0.76f;
        var body = new RectangleF(outer.X + (outer.Width - w) / 2f, outer.Y + (outer.Height - w) / 2f, w, w);
        using var path = MacTheme.RoundedRect(body, w * 0.24f);
        g.DrawPath(pen, path);
        var r = w * 0.08f;
        using var brush = new SolidBrush(pen.Color);
        g.FillEllipse(brush, body.X + body.Width / 2f - r, body.Y + body.Height / 2f - r, r * 2, r * 2);
    }

    /// <summary>SF Symbol “arrow.clockwise” 风格的刷新图标。</summary>
    public static void DrawRefresh(Graphics g, RectangleF rect, Color color, float weight = 1.7f)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, weight)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        var size = Math.Min(rect.Width, rect.Height);
        var r = size * 0.34f;
        var cx = rect.X + rect.Width / 2f;
        var cy = rect.Y + rect.Height / 2f;
        var box = new RectangleF(cx - r, cy - r, r * 2, r * 2);

        g.DrawArc(pen, box, -55, 290);

        // 箭头（在弧线起点附近）
        var angle = -55 * Math.PI / 180.0;
        var px = (float)(cx + r * Math.Cos(angle));
        var py = (float)(cy + r * Math.Sin(angle));
        var arrow = size * 0.16f;
        g.DrawLine(pen, px, py, px - arrow, py - arrow * 0.15f);
        g.DrawLine(pen, px, py, px - arrow * 0.15f, py - arrow);

        g.Restore(state);
    }
}
