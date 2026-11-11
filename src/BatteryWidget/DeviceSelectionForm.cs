using BatteryWidget.Core.Devices;
using BatteryWidget.Core.Services;

namespace BatteryWidget;

/// <summary>设备选择窗口（macOS 风格）：扫描可读电量的设备，勾选要显示的。</summary>
public sealed class DeviceSelectionForm : MacForm
{
    private readonly FlowLayoutPanel _list;
    private readonly Label _statusLabel;
    private readonly MacButton _okButton;
    private readonly MacButton _rescanButton;
    private readonly HashSet<string> _selected;

    /// <summary>用户确认后的勾选结果。</summary>
    public List<string> SelectedIds { get; } = new();

    public DeviceSelectionForm(IEnumerable<string> selectedIds)
    {
        _selected = new HashSet<string>(selectedIds);

        Text = "选择设备";
        ClientSize = new Size(620, 500);
        MinimumSize = new Size(520, 400);
        CloseToTray = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        // ---------- 标题区 ----------
        var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = MacTheme.Window };
        header.Controls.Add(new Label
        {
            Text = "选择要显示的设备",
            Font = MacTheme.Font(13f, FontStyle.Bold),
            ForeColor = MacTheme.Text,
            AutoSize = true,
            Location = new Point(22, 12),
            BackColor = Color.Transparent
        });
        _statusLabel = new Label
        {
            Text = "正在扫描设备…",
            Font = MacTheme.Font(8.75f),
            ForeColor = MacTheme.Secondary,
            AutoSize = true,
            Location = new Point(22, 40),
            BackColor = Color.Transparent
        };
        header.Controls.Add(_statusLabel);

        // ---------- 列表 ----------
        _list = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = MacTheme.Window,
            Padding = new Padding(22, 4, 22, 4)
        };
        _list.Resize += (_, _) => RelayoutRows();

        // ---------- 底部 ----------
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 74, BackColor = MacTheme.Window };
        footer.Paint += (_, e) =>
        {
            using var pen = new Pen(MacTheme.Border);
            e.Graphics.DrawLine(pen, 22, 0, footer.Width - 22, 0);
        };

        _rescanButton = new MacButton
        {
            Text = "重新扫描",
            Kind = MacButton.ButtonStyle.Plain,
            Size = new Size(96, 30),
            Location = new Point(22, 20)
        };
        _rescanButton.Click += (_, _) => StartScan();

        _okButton = new MacButton
        {
            Text = "确定",
            Kind = MacButton.ButtonStyle.Primary,
            Size = new Size(88, 32)
        };
        _okButton.Click += (_, _) => Accept();

        var cancel = new MacButton
        {
            Text = "取消",
            Kind = MacButton.ButtonStyle.Secondary,
            Size = new Size(88, 32)
        };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        footer.Resize += (_, _) =>
        {
            _okButton.Location = new Point(footer.Width - 22 - _okButton.Width, 18);
            cancel.Location = new Point(footer.Width - 22 - _okButton.Width - 10 - cancel.Width, 18);
        };

        footer.Controls.Add(_rescanButton);
        footer.Controls.Add(cancel);
        footer.Controls.Add(_okButton);

        Body.Controls.Add(_list);
        Body.Controls.Add(footer);
        Body.Controls.Add(header);

        Shown += (_, _) => StartScan();
    }

    private async void StartScan()
    {
        _rescanButton.Enabled = false;
        _okButton.Enabled = false;
        _statusLabel.Text = "正在扫描设备…";
        _list.Controls.Clear();
        var wasEmpty = _selected.Count == 0;

        IReadOnlyList<DeviceCandidate> candidates;
        try
        {
            candidates = await Task.Run(() => new DeviceDiscovery().Scan());
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "扫描失败：" + ex.Message;
            _rescanButton.Enabled = true;
            _okButton.Enabled = true;
            return;
        }

        foreach (var candidate in candidates)
        {
            var row = new DeviceRow
            {
                Text = candidate.Name,
                Kind = candidate.Kind,
                Subtitle = candidate.KindText + (candidate.Supported ? "" : " · 暂不支持"),
                Detail = candidate.Detail,
                Checked = wasEmpty ? candidate.Supported : _selected.Contains(candidate.Id),
                Tag = candidate.Id
            };
            _list.Controls.Add(row);
        }

        RelayoutRows();

        _statusLabel.Text = _list.Controls.Count == 0
            ? "没有扫描到可读电量的设备。设备休眠时唤醒后再点「重新扫描」。"
            : $"找到 {candidates.Count} 台设备，勾选后点确定。";

        _rescanButton.Enabled = true;
        _okButton.Enabled = true;
    }

    private void RelayoutRows()
    {
        var width = Math.Max(200, _list.ClientSize.Width - _list.Padding.Horizontal - 4);
        foreach (Control control in _list.Controls)
        {
            control.Width = width;
            control.Margin = new Padding(0, 0, 0, 8);
        }
    }

    private void Accept()
    {
        SelectedIds.Clear();
        foreach (Control control in _list.Controls)
        {
            if (control is DeviceRow { Checked: true } row && row.Tag is string id)
                SelectedIds.Add(id);
        }
        DialogResult = DialogResult.OK;
    }
}

