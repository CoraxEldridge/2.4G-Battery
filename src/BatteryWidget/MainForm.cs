using BatteryWidget.Core.Devices;
using BatteryWidget.Core.Services;

namespace BatteryWidget;

public sealed class MainForm : MacForm, IMessageFilter
{
    private const string RepositoryUrl = "https://github.com/CoraxEldridge/2.4G-Battery";
    private const int CardHeight = 158;
    private const int Gap = 0;
    private const int Edge = 22;

    private readonly DeviceService _service = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, DeviceCard> _cards = new();
    private readonly Dictionary<string, (BatteryReading Reading, DateTime At)> _lastGood = new();
    private readonly Dictionary<string, (string Name, DeviceKind Kind)> _known = new();
    private readonly Dictionary<string, bool> _lowNotified = new();
    private readonly TrayIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolTip _toolTip = new();
    private readonly Panel _content;
    private readonly Font _emptyFont = MacTheme.Font(10f);
    private readonly MacIconButton _refreshButton;
    private readonly MacButton _selectButton;
    private readonly MacSegmentedControl _refreshMode;
    private readonly MacSwitch _autoStart;
    private readonly Label _statusLabel;
    private readonly Label _subtitle;

    private bool _busy;

    public MainForm()
    {
        _settings = AppSettings.Load();

        Text = "2.4G Battery";
        ClientSize = new Size(700, 596);
        MinimumSize = new Size(420, 360);
        CloseToTray = true;
        BackColor = MacTheme.Window;

        // ---------- 标题栏工具栏按钮 ----------
        _refreshButton = new MacIconButton();
        _refreshButton.DrawIcon = (g, rect, color) => SfIcon.DrawRefresh(g, rect, color);
        _refreshButton.Click += (_, _) => RefreshDevices(deep: true);
        _toolTip.SetToolTip(_refreshButton, "立即刷新");
        AddTitleBarControl(_refreshButton);

        // ---------- 顶部 ----------
        var header = new Panel { Dock = DockStyle.Top, Height = 88, BackColor = Color.Transparent };
        var titleLabel = new GrayscaleLabel
        {
            Text = "设备",
            Font = MacTheme.Font(19f, FontStyle.Bold),
            ForeColor = MacTheme.Text,
            Location = new Point(Edge, 6),
            BackColor = Color.Transparent
        };
        titleLabel.FitToText();
        header.Controls.Add(titleLabel);
        _subtitle = new Label
        {
            Text = "实时读取 2.4G 接收器上的设备电量",
            Font = MacTheme.Font(9f),
            ForeColor = MacTheme.Secondary,
            AutoSize = true,
            Location = new Point(Edge + 1, 44),
            BackColor = Color.Transparent
        };
        header.Controls.Add(_subtitle);

        _selectButton = new MacButton
        {
            Text = "选择设备",
            Kind = MacButton.ButtonStyle.Secondary,
            Size = new Size(96, 30)
        };
        _selectButton.Click += (_, _) => SelectDevices();
        header.Resize += (_, _) =>
            _selectButton.Location = new Point(header.Width - Edge - _selectButton.Width, 26);
        header.Controls.Add(_selectButton);

        // ---------- 底部 ----------
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 100, BackColor = Color.Transparent };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(MacTheme.Border);
            e.Graphics.DrawLine(pen, Edge, 0, footer.Width - Edge, 0);
        };

        footer.Controls.Add(new Label
        {
            Text = "刷新",
            Font = MacTheme.Font(9f),
            ForeColor = MacTheme.Text,
            AutoSize = true,
            Location = new Point(Edge, 22),
            BackColor = Color.Transparent
        });

        _refreshMode = new MacSegmentedControl
        {
            Items = new[] { "关闭", "5 秒", "30 秒" },
            Size = new Size(168, 28),
            Location = new Point(Edge + 40, 16),
            SelectedIndex = _settings.RefreshSeconds switch { 0 => 0, >= 30 => 2, _ => 1 }
        };
        _refreshMode.SelectionChanged += (_, _) =>
        {
            _settings.RefreshSeconds = _refreshMode.SelectedIndex switch { 0 => 0, 1 => 5, _ => 30 };
            _settings.Save();
            ApplyTimerInterval();
            Relayout();
        };
        footer.Controls.Add(_refreshMode);

        var autoStartLabel = new Label
        {
            Text = "开机自启",
            Font = MacTheme.Font(9f),
            ForeColor = MacTheme.Text,
            AutoSize = true,
            Location = new Point(Edge + 240, 22),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        footer.Controls.Add(autoStartLabel);
        _autoStart = new MacSwitch
        {
            Location = new Point(Edge + 308, 17),
            Checked = AutoStart.IsEnabled
        };
        _autoStart.CheckedChanged += (_, _) =>
        {
            try
            {
                AutoStart.Set(_autoStart.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "写入开机自启失败：" + ex.Message, "2.4G Battery",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        autoStartLabel.Click += (_, _) => _autoStart.Checked = !_autoStart.Checked;
        footer.Controls.Add(_autoStart);

        // 右侧：GitHub 项目链接
        var githubLink = new LinkLabel
        {
            Text = "GitHub ↗",
            Font = MacTheme.Font(9f),
            LinkColor = MacTheme.Secondary,
            ActiveLinkColor = MacTheme.Blue,
            VisitedLinkColor = MacTheme.Secondary,
            LinkBehavior = LinkBehavior.HoverUnderline,
            AutoSize = true,
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        githubLink.LinkClicked += (_, _) => OpenRepository();
        footer.Controls.Add(githubLink);
        footer.Resize += (_, _) =>
            githubLink.Location = new Point(footer.Width - Edge - githubLink.Width, 17);
        githubLink.Location = new Point(footer.Width - Edge - githubLink.Width, 17);

        _statusLabel = new Label
        {
            Text = "正在读取…",
            Font = MacTheme.Font(8.5f),
            ForeColor = MacTheme.Tertiary,
            AutoSize = true,
            Location = new Point(Edge, 64),
            BackColor = Color.Transparent
        };
        footer.Controls.Add(_statusLabel);

        // ---------- 内容 ----------
        _content = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            AutoScroll = false
        };
        _content.Resize += (_, _) => Relayout();
        Application.AddMessageFilter(this);
        _content.Paint += PaintContent;

        Body.Controls.Add(_content);
        Body.Controls.Add(footer);
        Body.Controls.Add(header);

        // ---------- 托盘 ----------
        _tray = new TrayIcon();
        _tray.ShowRequested += ShowFromTray;
        _tray.RefreshRequested += () => RefreshDevices(deep: true);
        _tray.ExitRequested += ForceClose;

        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += (_, _) =>
        {
            // 罗技设备休眠后不再主动上报，需要周期性唤醒才能读到充电状态；
            // 唤醒会耗电，所以限制为最多 30 秒一次。
            var needsWake = _asleep.Any(id => id.StartsWith("logitech:", StringComparison.Ordinal))
                            && DateTime.UtcNow - _lastWakeAttempt > TimeSpan.FromSeconds(30);

            if (needsWake)
                _lastWakeAttempt = DateTime.UtcNow;

            RefreshDevices(deep: needsWake);
        };

        VisibleChanged += (_, _) =>
        {
            if (!Visible)
                _tray.UpdateTooltip("2.4G Battery · 已最小化到托盘");
        };

        Shown += async (_, _) =>
        {
            ApplyTimerInterval();
            await EnsureFirstRunSelectionAsync();
            RebuildCards();
            RefreshDevices(deep: true);
        };
    }

    private void ApplyTimerInterval()
    {
        _timer.Stop();
        if (_settings.RefreshSeconds <= 0)
            return;
        _timer.Interval = Math.Max(1000, _settings.RefreshSeconds * 1000);
        _timer.Start();
    }

    private void PaintContent(object? sender, PaintEventArgs e)
    {
        if (_cards.Count > 0)
            return;

        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var brush = new SolidBrush(MacTheme.Tertiary);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        var rect = new RectangleF(0, _content.ClientSize.Height / 2f - 50, _content.ClientSize.Width, 100);
        e.Graphics.DrawString(
            "还没有选择设备\r\n\r\n点右上角「选择设备」扫描可读电量的 2.4G 设备",
            _emptyFont, brush, rect, format);
    }

    /// <summary>第一次运行：自动扫描并勾选所有能读电量的设备，之后以用户选择为准。</summary>
    private async Task EnsureFirstRunSelectionAsync()
    {
        if (_settings.FirstRunCompleted)
            return;

        _statusLabel.Text = "首次运行，正在扫描设备…";
        try
        {
            var candidates = await Task.Run(() => new DeviceDiscovery().Scan());
            _settings.SelectedDevices = candidates
                .Where(c => c.Supported)
                .Select(c => c.Id)
                .Distinct()
                .ToList();
        }
        catch
        {
            _settings.SelectedDevices = new List<string>();
        }

        _settings.FirstRunCompleted = true;
        _settings.Save();
    }

    private void SelectDevices()
    {
        _timer.Stop();
        try
        {
            using var dialog = new DeviceSelectionForm(_settings.SelectedDevices);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            _settings.SelectedDevices = dialog.SelectedIds.Distinct().ToList();
            _settings.Save();
            RebuildCards();
            Relayout();
        }
        finally
        {
            ApplyTimerInterval();
        }

        RefreshDevices(deep: true);
    }

    private readonly HashSet<string> _asleep = new();
    private int _scrollY;
    private int _maxScroll;
    private DateTime _lastWakeAttempt = DateTime.MinValue;

    private async void RefreshDevices(bool deep)
    {
        if (_busy)
            return;

        if (_settings.SelectedDevices.Count == 0)
        {
            RebuildCards();
            _statusLabel.Text = "未选择设备";
            return;
        }

        _busy = true;
        if (deep)
        {
            _refreshButton.Enabled = false;
            _statusLabel.Text = "正在唤醒并读取…";
        }

        try
        {
            var ids = new HashSet<string>(_settings.SelectedDevices);
            var request = new DeviceReadRequest(deep, ids);
            var snapshots = await Task.Run(() => _service.ReadAll(request));
            Apply(snapshots);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "读取失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            if (deep)
                _refreshButton.Enabled = true;
        }
    }

    private void Apply(IReadOnlyList<DeviceSnapshot> snapshots)
    {
        var current = snapshots.ToDictionary(s => s.Id);
        foreach (var id in _settings.SelectedDevices)
        {
            if (!current.TryGetValue(id, out var snapshot))
            {
                var known = _known.GetValueOrDefault(id);
                snapshot = new DeviceSnapshot(
                    id,
                    string.IsNullOrWhiteSpace(known.Name) ? id : known.Name,
                    known.Kind,
                    "2.4G",
                    null,
                    Online: false,
                    Error: "设备未响应，可能正在休眠");
            }
            else
            {
                _known[id] = (snapshot.Name, snapshot.Kind);
                if (snapshot.Battery?.Percent is not null)
                {
                    // 部分罗技设备充电时仍上报"放电中"（实测插着线电量从 70% 涨到 90%，
                    // 状态字节却一直是 0），这里用电量上升做一次兜底判断。
                    var previous = LastGood(id)?.Percent;
                    if (snapshot.Battery.Status == BatteryStatus.Discharging
                        && previous is { } before
                        && snapshot.Battery.Percent is { } now
                        && now > before)
                    {
                        snapshot = snapshot with
                        {
                            Battery = snapshot.Battery with { Status = BatteryStatus.Charging }
                        };
                    }

                    RecordGood(id, snapshot.Battery);
                }
            }

            if (snapshot.Online)
                _asleep.Remove(id);
            else
                _asleep.Add(id);

            var card = GetOrCreateCard(id, snapshot);
            card.Apply(snapshot, LastGood(id), LastAt(id));
            CheckLowBattery(id, snapshot.Name, snapshot.Battery?.Percent);
        }

        Relayout();
        UpdateTray();
        _statusLabel.Text = $"上次更新 {DateTime.Now:HH:mm:ss}";
    }

    private DeviceCard GetOrCreateCard(string id, DeviceSnapshot snapshot)
    {
        if (_cards.TryGetValue(id, out var existing))
        {
            existing.Kind = snapshot.Kind;
            return existing;
        }

        var card = new DeviceCard
        {
            Kind = snapshot.Kind,
            FallbackName = snapshot.Name,
            FallbackKind = snapshot.KindText
        };
        _cards[id] = card;
        _content.Controls.Add(card);
        return card;
    }

    private void RebuildCards()
    {
        var keep = new HashSet<string>(_settings.SelectedDevices);
        foreach (var id in _cards.Keys.ToList())
        {
            if (keep.Contains(id))
                continue;
            _content.Controls.Remove(_cards[id]);
            _cards[id].Dispose();
            _cards.Remove(id);
        }

        Relayout();
        UpdateTray();
    }

    /// <summary>内容区滚轮滚动（不显示滚动条，避免布局抖动）。</summary>
    public bool PreFilterMessage(ref Message m)
    {
        const int WM_MOUSEWHEEL = 0x020A;
        if (m.Msg != WM_MOUSEWHEEL || _content is null || _content.IsDisposed)
            return false;

        var client = _content.PointToClient(Cursor.Position);
        if (!_content.ClientRectangle.Contains(client))
            return false;

        var delta = (short)((long)m.WParam >> 16);
        if (_maxScroll <= 0)
            return false;

        _scrollY = Math.Clamp(_scrollY - delta / 120 * 56, 0, _maxScroll);
        Relayout();
        return true;
    }

    private void Relayout()
    {
        if (_content.Width <= 0 || _content.Height <= 0)
            return;

        var width = Math.Max(180, _content.Width - Edge * 2);
        var count = _settings.SelectedDevices.Count;
        var contentHeight = 4 + count * (CardHeight + Gap) - Gap + 8;

        _maxScroll = Math.Max(0, contentHeight - _content.Height);
        _scrollY = Math.Clamp(_scrollY, 0, _maxScroll);

        var y = 4 - _scrollY;
        foreach (var id in _settings.SelectedDevices)
        {
            if (!_cards.TryGetValue(id, out var card))
                continue;
            card.SetBounds(Edge - 8, y, width + 16, CardHeight);
            y += CardHeight + Gap;
        }

        _content.Invalidate();

        _subtitle.Text = _settings.SelectedDevices.Count == 0
            ? "实时读取 2.4G 接收器上的设备电量"
            : $"已选择 {_settings.SelectedDevices.Count} 台设备" +
              (_settings.RefreshSeconds > 0 ? $" · 每 {_settings.RefreshSeconds} 秒自动读取" : " · 自动刷新已关闭");
    }

    private void UpdateTray()
    {
        var parts = new List<string>();
        int? lowest = null;
        var charging = false;
        foreach (var id in _settings.SelectedDevices)
        {
            var reading = _lastGood.GetValueOrDefault(id).Reading;
            var name = _known.GetValueOrDefault(id);
            if (reading?.Percent is not { } percent)
                continue;

            var shortName = ShortName(string.IsNullOrWhiteSpace(name.Name) ? id : name.Name);
            parts.Add($"{shortName} {percent}%");
            lowest = lowest is null ? percent : Math.Min(lowest.Value, percent);
            if (reading.Status == BatteryStatus.Charging)
                charging = true;
        }

        _tray.UpdateTooltip(parts.Count > 0 ? "2.4G Battery：" + string.Join(" · ", parts) : "2.4G Battery");
        _tray.UpdateIcon(lowest, charging);
    }

    private static void OpenRepository()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(RepositoryUrl)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 打不开浏览器就忽略
        }
    }

    private static string ShortName(string name)
    {
        if (name.StartsWith("迈从", StringComparison.Ordinal))
            return "迈从";
        if (name.StartsWith("Pop-Up", StringComparison.OrdinalIgnoreCase))
            return "罗技触摸板";
        var index = name.IndexOfAny(new[] { ' ', '(', '（' });
        return index > 0 ? name[..index] : name;
    }

    /// <summary>低于 20% 时发一次 Windows 系统通知，回到 25% 以上后重置。</summary>
    private void CheckLowBattery(string key, string name, int? percent)
    {
        if (percent is null)
            return;

        var notified = _lowNotified.GetValueOrDefault(key);
        if (percent < 20 && !notified)
        {
            _tray.Notify("电量偏低", $"{name} 剩余 {percent}%，建议尽快充电。");
            _lowNotified[key] = true;
        }
        else if (percent >= 25 && notified)
        {
            _lowNotified[key] = false;
        }
    }

    private BatteryReading? LastGood(string key) => _lastGood.TryGetValue(key, out var v) ? v.Reading : null;

    private DateTime? LastAt(string key) => _lastGood.TryGetValue(key, out var v) ? v.At : null;

    private void RecordGood(string key, BatteryReading reading) => _lastGood[key] = (reading, DateTime.Now);

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _toolTip.Dispose();
            _tray.Dispose();
            _emptyFont.Dispose();
        }
        base.Dispose(disposing);
    }
}





