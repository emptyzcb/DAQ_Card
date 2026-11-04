using System.Diagnostics;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private readonly TrendRecorder trendRecorder = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IndustrialFieldDataAcquisitionTerminal", "TrendSessions"));
    private readonly TrendChartControl trendChart = new();
    private readonly BufferedTrendTreeView trendChannelTree = new();
    private readonly Panel trendLaneViewport = new();
    private readonly ComboBox trendWindowInput = new();
    private readonly Button trendZoomInButton = new();
    private readonly Button trendZoomOutButton = new();
    private readonly Button trendPauseButton = new();
    private readonly Button trendExportButton = new();
    private readonly Label trendStatusLabel = new();
    private readonly Label trendEmptyLabel = new();
    private readonly Dictionary<string, TrendChannel> trendChannels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TreeNode> trendChannelNodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> selectedTrendKeys = new(StringComparer.Ordinal) { "LOOP:SET", "LOOP:IN" };
    private readonly Dictionary<string, (Panel Panel, TrendChartControl Chart, Label Title, Button Expand)> trendLanes = new(StringComparer.Ordinal);
    private readonly ToolTip trendToolTip = new();
    private bool updatingTrendTree;
    private bool trendPaused;
    private bool trendRecordingFailed;
    private DateTime lastTrendPaintUtc;
    private DateTime lastTrendStatusUtc;
    private TimeSpan? manualTrendAxisWindow;
    private DateTime lastTrendAxisEnd;
    private TimeSpan lastTrendAxisWindow;
    private string? expandedTrendUnit;
    private Dictionary<string, IReadOnlyList<TrendPoint>>? frozenTrendPoints;
    private List<TrendChannel>? frozenTrendChannels;

    private Control BuildTrendPage()
    {
        trendRecorder.RecordingError += ex => Ui(() =>
        {
            if (IsDisposed || Disposing) return;
            trendRecordingFailed = true;
            trendStatusLabel.Text = $"趋势记录已停止：{ex.Message}";
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  趋势记录已停止：{ex.Message}");
        });
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = UiPalette.Surface };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(8, 5, 0, 0), BackColor = UiPalette.Surface
        };
        toolbar.Controls.Add(ToolbarLabel("窗口"));
        ConfigureTrendCombo(trendWindowInput, 86);
        trendWindowInput.Items.AddRange(new object[] { "30 秒", "2 分钟", "10 分钟" });
        trendWindowInput.SelectedIndexChanged += (_, _) =>
        {
            ResetTrendView();
            RefreshTrendChart();
        };
        toolbar.Controls.Add(trendWindowInput);
        ConfigureTrendButton(trendZoomInButton, "+", 31);
        ConfigureTrendButton(trendZoomOutButton, "−", 31);
        trendToolTip.SetToolTip(trendZoomInButton, "放大时间轴");
        trendToolTip.SetToolTip(trendZoomOutButton, "缩小时间轴");
        trendZoomInButton.Click += (_, _) => trendWindowInput.SelectedIndex = Math.Max(0, trendWindowInput.SelectedIndex - 1);
        trendZoomOutButton.Click += (_, _) => trendWindowInput.SelectedIndex = Math.Min(trendWindowInput.Items.Count - 1, trendWindowInput.SelectedIndex + 1);
        toolbar.Controls.Add(trendZoomInButton);
        toolbar.Controls.Add(trendZoomOutButton);
        ConfigureTrendButton(trendPauseButton, "暂停查看", 94);
        trendPauseButton.Click += (_, _) =>
        {
            if (!trendPaused)
            {
                frozenTrendChannels = AllTrendChannels().ToList();
                frozenTrendPoints = frozenTrendChannels.ToDictionary(
                    channel => channel.Key, channel => trendRecorder.GetPoints(channel.Key), StringComparer.Ordinal);
            }
            else
            {
                frozenTrendChannels = null;
                frozenTrendPoints = null;
            }
            trendPaused = !trendPaused;
            trendPauseButton.Text = trendPaused ? "继续查看" : "暂停查看";
            trendPauseButton.BackColor = trendPaused ? Color.FromArgb(222, 245, 238) : UiPalette.Surface;
            RefreshTrendChannels();
            UpdateTrendStatus();
        };
        toolbar.Controls.Add(trendPauseButton);
        ConfigureTrendButton(trendExportButton, "导出 CSV", 92);
        trendExportButton.Enabled = false;
        trendExportButton.Click += (_, _) => ExportTrendCsv();
        toolbar.Controls.Add(trendExportButton);
        var newSessionButton = new Button();
        ConfigureTrendButton(newSessionButton, "新建记录", 92);
        newSessionButton.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "开始新记录？当前记录文件会保留在本机。", "新建记录",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                trendRecorder.StartNewSession();
                ResetTrendView();
                trendRecordingFailed = false;
                trendPaused = false;
                frozenTrendChannels = null;
                frozenTrendPoints = null;
                trendPauseButton.Text = "暂停查看";
                trendPauseButton.BackColor = UiPalette.Surface;
                trendExportButton.Enabled = false;
                RefreshTrendChannels();
                UpdateTrendStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "无法新建记录", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        toolbar.Controls.Add(newSessionButton);
        var folderButton = new Button();
        ConfigureTrendButton(folderButton, "记录目录", 86);
        folderButton.Click += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetDirectoryName(trendRecorder.SessionPath)!, UseShellExecute = true
        });
        toolbar.Controls.Add(folderButton);
        var waveformArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = UiPalette.Surface, Margin = new Padding(0, 6, 0, 0)
        };
        waveformArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        waveformArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 246));
        trendLaneViewport.Dock = DockStyle.Fill;
        trendLaneViewport.AutoScroll = true;
        trendLaneViewport.BackColor = UiPalette.Surface;
        trendLaneViewport.Resize += (_, _) => LayoutTrendLanes();
        trendEmptyLabel.Dock = DockStyle.Fill;
        trendEmptyLabel.Text = "选择右侧数据通道";
        trendEmptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        trendEmptyLabel.ForeColor = Color.FromArgb(123, 139, 153);
        trendLaneViewport.Controls.Add(trendEmptyLabel);
        waveformArea.Controls.Add(trendLaneViewport, 0, 0);
        waveformArea.Controls.Add(BuildTrendSelector(), 1, 0);
        trendStatusLabel.Dock = DockStyle.Fill;
        trendStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        trendStatusLabel.ForeColor = UiPalette.Muted;
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(waveformArea, 0, 1);
        layout.Controls.Add(trendStatusLabel, 0, 2);
        trendWindowInput.SelectedIndex = 1;
        RefreshTrendChannels();
        UpdateTrendStatus();
        return layout;
    }

    private Control BuildTrendSelector()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiPalette.Canvas };
        var heading = new Label
        {
            Text = "数据通道", Dock = DockStyle.Top, Height = 43, Padding = new Padding(12, 13, 0, 0),
            Font = new Font(Font, FontStyle.Bold), ForeColor = UiPalette.Ink
        };
        trendChannelTree.Dock = DockStyle.Fill;
        trendChannelTree.CheckBoxes = true;
        trendChannelTree.BorderStyle = BorderStyle.None;
        trendChannelTree.BackColor = panel.BackColor;
        trendChannelTree.ForeColor = UiPalette.Ink;
        trendChannelTree.ItemHeight = 26;
        trendChannelTree.HideSelection = false;
        trendChannelTree.AfterCheck += (_, e) =>
        {
            if (updatingTrendTree || e.Node is null) return;
            var checkedNode = e.Node;
            updatingTrendTree = true;
            try
            {
                if (checkedNode.Tag is TrendChannel channel)
                {
                    if (checkedNode.Checked) selectedTrendKeys.Add(channel.Key);
                    else selectedTrendKeys.Remove(channel.Key);
                    checkedNode.Parent!.Checked = checkedNode.Parent.Nodes.Cast<TreeNode>().Any(node => node.Checked);
                }
                else
                {
                    foreach (TreeNode child in checkedNode.Nodes)
                    {
                        child.Checked = checkedNode.Checked;
                        var item = (TrendChannel)child.Tag!;
                        if (child.Checked) selectedTrendKeys.Add(item.Key);
                        else selectedTrendKeys.Remove(item.Key);
                    }
                }
            }
            finally { updatingTrendTree = false; }
            RefreshTrendChart();
        };
        panel.Controls.Add(trendChannelTree);
        panel.Controls.Add(heading);
        return panel;
    }

    private static Label ToolbarLabel(string value) => new()
    {
        Text = value, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(4, 9, 2, 0), ForeColor = UiPalette.Muted
    };

    private static void ConfigureTrendCombo(ComboBox combo, int width)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Width = width;
        combo.Height = 30;
        combo.Margin = new Padding(0, 3, 10, 0);
    }

    private static void ConfigureTrendButton(Button button, string text, int width)
    {
        button.Text = text;
        button.Width = width;
        button.Height = 30;
        button.Margin = new Padding(2, 3, 4, 0);
        button.FlatStyle = FlatStyle.Flat;
        UiPalette.StyleButton(button);
    }

    private IEnumerable<TrendChannel> AllTrendChannels()
    {
        var live = new[] { "电流回环", "AD7606", "数字输入", "模拟输出", "RS485" }
            .SelectMany(category => trendRecorder.GetChannels(category)).ToList();
        // Configured channels remain selectable before the first sample, including after reopening a project.
        try
        {
            var sensors = ReadRs485ConfigurationFromGrid();
            for (var index = 0; index < sensors.Count; index++)
                for (var offset = 0; offset < sensors[index].Quantity; offset++)
                {
                    var sensor = sensors[index];
                    var key = $"RS:{index}:{sensor.SlaveAddress}:{sensor.Function:X2}:{offset}";
                    if (!live.Any(channel => channel.Key == key))
                        live.Add(new TrendChannel(key, "RS485", $"{sensor.Name} R{offset + 1} (addr {sensor.SlaveAddress})", "raw"));
                }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException or OverflowException)
        { /* An unfinished grid edit must not interrupt chart painting. */ }
        foreach (var (source, config) in EffectiveEngineeringChannels())
            if (!live.Any(channel => channel.Key == "ENG:" + source))
                live.Add(new TrendChannel("ENG:" + source, source.StartsWith("AD:") ? "AD7606" : "RS485", config.Name, config.Unit));
        return live;
    }

    private static string TrendUnitKey(TrendChannel channel) => channel.Unit;

    private void RefreshTrendChannels()
    {
        var available = (frozenTrendChannels ?? AllTrendChannels().ToList()).ToArray();
        // Keep selected RS485 channels until their first live sample arrives after project restore.
        trendChannels.Clear();
        trendChannelNodes.Clear();
        updatingTrendTree = true;
        trendChannelTree.BeginUpdate();
        try
        {
            trendChannelTree.Nodes.Clear();
            foreach (var group in available.GroupBy(TrendUnitKey))
            {
                var root = new TreeNode($"{group.Key}  ·  {group.Count()} 通道") { Tag = group.Key };
                foreach (var channel in group)
                {
                    trendChannels[channel.Key] = channel;
                    var child = new TreeNode(channel.Name) { Tag = channel, Checked = selectedTrendKeys.Contains(channel.Key) };
                    root.Nodes.Add(child);
                    trendChannelNodes[channel.Key] = child;
                }
                root.Checked = root.Nodes.Cast<TreeNode>().Any(child => child.Checked);
                trendChannelTree.Nodes.Add(root);
                root.Expand();
            }
        }
        finally
        {
            trendChannelTree.EndUpdate();
            updatingTrendTree = false;
        }
        RefreshTrendChart();
        UpdateTrendStatus();
    }

    private void RecordTrendSnapshot(AcquisitionSnapshot snapshot)
    {
        if (trendRecordingFailed || IsDisposed || Disposing) return;
        try
        {
            trendRecorder.SetEngineeringChannels(EffectiveEngineeringChannels());
            trendRecorder.AddSnapshot(snapshot);
            if (!trendExportButton.Enabled) trendExportButton.Enabled = true;
            if (!trendPaused && contentTabs.SelectedTab?.Text == "趋势记录" &&
                snapshot.Rs485Sensors is not null && trendRecorder.GetChannels("RS485")
                    .Any(channel => !trendChannels.ContainsKey(channel.Key)))
                RefreshTrendChannels();
            if (!trendPaused && contentTabs.SelectedTab?.Text == "趋势记录" &&
                DateTime.UtcNow - lastTrendPaintUtc >= TimeSpan.FromMilliseconds(200))
            {
                RefreshTrendChart();
                lastTrendPaintUtc = DateTime.UtcNow;
            }
            if (contentTabs.SelectedIndex == 3 && DateTime.UtcNow - lastTrendStatusUtc >= TimeSpan.FromMilliseconds(500))
            {
                UpdateTrendStatus();
                lastTrendStatusUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            trendRecordingFailed = true;
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  趋势记录已停止：{ex.Message}");
            trendStatusLabel.Text = $"趋势记录已停止：{ex.Message}";
        }
    }

    private void RefreshTrendChart()
    {
        var duration = trendWindowInput.SelectedIndex switch
        {
            0 => TimeSpan.FromSeconds(30),
            2 => TimeSpan.FromMinutes(10),
            _ => TimeSpan.FromMinutes(2)
        };
        trendZoomInButton.Enabled = trendWindowInput.SelectedIndex > 0;
        trendZoomOutButton.Enabled = trendWindowInput.SelectedIndex < trendWindowInput.Items.Count - 1;
        var entries = trendChannels.Values.Where(channel => selectedTrendKeys.Contains(channel.Key))
            .Select(channel => new TrendSeries(channel,
                frozenTrendPoints?.GetValueOrDefault(channel.Key) ?? trendRecorder.GetPoints(channel.Key),
                TrendChartControl.SeriesColor(channel))).ToArray();
        var populated = entries.Where(entry => entry.Points.Count > 0).ToArray();
        var end = populated.Length == 0 ? default : populated.Max(entry => entry.Points[^1].Timestamp);
        var newest = end;
        var earliest = populated.Length == 0 ? default : populated.Min(entry => entry.Points[0].Timestamp);
        var elapsed = end == default ? 0 : Math.Max(0, (end - earliest).TotalSeconds);
        var plotWindow = TimeSpan.FromSeconds(Math.Clamp(elapsed * 1.15, 10, duration.TotalSeconds));
        if (manualTrendAxisWindow.HasValue) plotWindow = manualTrendAxisWindow.Value;
        lastTrendAxisEnd = end;
        lastTrendAxisWindow = plotWindow;
        var visibleUnits = entries.Select(entry => TrendUnitKey(entry.Channel)).Distinct().ToArray();
        var hasExpandedUnit = expandedTrendUnit is not null && visibleUnits.Contains(expandedTrendUnit);
        foreach (var unit in visibleUnits)
        {
            var lane = EnsureTrendLane(unit);
            var grouped = entries.Where(entry => TrendUnitKey(entry.Channel) == unit).ToArray();
            lane.Title.Text = $"{unit}  ·  {grouped.Length} 通道";
            lane.Chart.SetSeries(grouped, duration, end, plotWindow, unit, newest);
        }
        foreach (var (unit, lane) in trendLanes)
        {
            lane.Panel.Visible = visibleUnits.Contains(unit) &&
                (!hasExpandedUnit || expandedTrendUnit == unit);
            lane.Expand.Text = expandedTrendUnit == unit ? "▣" : "□";
            trendToolTip.SetToolTip(lane.Expand, expandedTrendUnit == unit ? "恢复全部画布" : "放大此画布");
        }
        trendEmptyLabel.Visible = visibleUnits.Length == 0;
        UpdateTrendTreeValues(entries);
        LayoutTrendLanes();
    }

    private (Panel Panel, TrendChartControl Chart, Label Title, Button Expand) EnsureTrendLane(string unit)
    {
        if (trendLanes.TryGetValue(unit, out var lane)) return lane;
        var panel = new Panel { BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var heading = new Panel { Dock = DockStyle.Top, Height = 27, BackColor = Color.FromArgb(244, 247, 250) };
        var title = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0), ForeColor = Color.FromArgb(40, 58, 75) };
        var expand = new Button { Dock = DockStyle.Right, Width = 31, Text = "□", FlatStyle = FlatStyle.Flat,
            BackColor = heading.BackColor, TabStop = false };
        expand.FlatAppearance.BorderSize = 0;
        expand.Click += (_, _) => { expandedTrendUnit = expandedTrendUnit == unit ? null : unit; MarkWorkbenchDirty(); RefreshTrendChart(); };
        heading.Controls.Add(title);
        heading.Controls.Add(expand);
        var chart = trendLanes.Count == 0 ? trendChart : new TrendChartControl();
        chart.Dock = DockStyle.Fill;
        chart.TimeZoomRequested += ZoomTrendTimeAxis;
        chart.ViewResetRequested += (_, _) =>
        {
            ResetTrendView();
            RefreshTrendChart();
        };
        panel.Controls.Add(chart);
        panel.Controls.Add(heading);
        trendLaneViewport.Controls.Add(panel);
        lane = (panel, chart, title, expand);
        trendLanes.Add(unit, lane);
        return lane;
    }

    private void ZoomTrendTimeAxis(double factor)
    {
        if (lastTrendAxisEnd == default || lastTrendAxisWindow <= TimeSpan.Zero) return;
        var maxWindow = trendWindowInput.SelectedIndex switch
        {
            0 => TimeSpan.FromSeconds(30),
            2 => TimeSpan.FromMinutes(10),
            _ => TimeSpan.FromMinutes(2)
        };
        var newTicks = (long)Math.Clamp(lastTrendAxisWindow.Ticks * factor,
            TimeSpan.FromMilliseconds(100).Ticks, maxWindow.Ticks);
        manualTrendAxisWindow = TimeSpan.FromTicks(newTicks);
        MarkWorkbenchDirty();
        RefreshTrendChart();
    }

    private void ResetTrendView()
    {
        manualTrendAxisWindow = null;
        foreach (var lane in trendLanes.Values) lane.Chart.ResetValueZoom();
    }

    private void LayoutTrendLanes()
    {
        var lanes = trendLanes.Values.Where(lane => lane.Panel.Visible).ToArray();
        if (lanes.Length == 0) return;
        var height = expandedTrendUnit is not null ? Math.Max(150, trendLaneViewport.ClientSize.Height - 4) :
            Math.Max(150, (trendLaneViewport.ClientSize.Height - 4) / lanes.Length);
        var width = Math.Max(100, trendLaneViewport.ClientSize.Width -
            (height * lanes.Length > trendLaneViewport.ClientSize.Height ? SystemInformation.VerticalScrollBarWidth : 0));
        trendLaneViewport.SuspendLayout();
        try
        {
            for (var index = 0; index < lanes.Length; index++)
            {
                var bounds = new Rectangle(0, index * height, width, height);
                if (lanes[index].Panel.Bounds != bounds) lanes[index].Panel.Bounds = bounds;
            }
            var scrollSize = new Size(0, height * lanes.Length);
            if (trendLaneViewport.AutoScrollMinSize != scrollSize) trendLaneViewport.AutoScrollMinSize = scrollSize;
        }
        finally { trendLaneViewport.ResumeLayout(); }
    }

    private void UpdateTrendTreeValues(IReadOnlyList<TrendSeries> entries)
    {
        foreach (var entry in entries)
        {
            if (!trendChannelNodes.TryGetValue(entry.Channel.Key, out var node)) continue;
            var latest = entry.Points.LastOrDefault(point => !double.IsNaN(point.Value));
            var value = latest == default ? "--" : $"{latest.Value:0.###}";
            var text = $"{entry.Channel.Name}   {value} {entry.Channel.Unit}";
            var color = latest != default && !latest.IsValid ? Color.FromArgb(185, 111, 39) : entry.Color;
            if (node.Text != text) node.Text = text;
            if (node.ForeColor != color) node.ForeColor = color;
        }
    }

    private void UpdateTrendStatus()
    {
        trendStatusLabel.Text = $"{(trendPaused ? "暂停查看 · " : "")}已接收 {trendRecorder.SnapshotCount} 次快照 · 横轴：上位机接收时间 · " +
            $"CSV 会话：{Path.GetFileName(trendRecorder.SessionPath)}";
    }

    private async void ExportTrendCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV 文件 (*.csv)|*.csv", DefaultExt = "csv",
            FileName = $"采集趋势_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await trendRecorder.ExportAsync(dialog.FileName);
            SetStatus($"趋势 CSV 已导出：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class BufferedTrendTreeView : TreeView
{
    private const int TvmSetExtendedStyle = 0x112C;
    private const int TvsExDoubleBuffer = 0x0004;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr mask, IntPtr style);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SendMessage(Handle, TvmSetExtendedStyle,
            new IntPtr(TvsExDoubleBuffer), new IntPtr(TvsExDoubleBuffer));
    }
}
