using System.Globalization;
using System.Text;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private readonly ICanAdapter canAdapter = new PcanAdapter();
    private readonly System.Windows.Forms.Timer canTimer = new() { Interval = 50 };
    private readonly ComboBox canChannelInput = new();
    private readonly ComboBox canBaudInput = new();
    private readonly ComboBox canFrameTypeInput = new();
    private readonly TextBox canIdInput = new();
    private readonly TextBox canDataInput = new();
    private readonly NumericUpDown canRemoteDlcInput = new();
    private readonly CheckBox canRemoteInput = new();
    private readonly Button canConnectButton = new();
    private readonly Button canRefreshButton = new();
    private readonly Button canSendButton = new();
    private readonly Label canStatusLabel = new();
    private readonly DataGridView canGrid = new();
    private readonly DataGridView canTransmitGrid = new();
    private readonly TextBox canDetails = new();
    private readonly Label canCountLabel = new();
    private readonly CheckBox canFollowInput = new();
    private readonly ComboBox canViewInput = new();
    private readonly CheckBox canListenOnly = new();
    private readonly TextBox canFilterFrom = new();
    private readonly TextBox canFilterTo = new();
    private readonly CheckBox canFilterEnabled = new();
    private readonly Button canTraceButton = new();
    private readonly Label canTraceStatus = new();
    private CanTraceRecorder? canTrace;
    private bool canTraceFinishing;
    private string? canTracePath;
    private long canTraceCount;
    private uint canFilterLow, canFilterHigh = 0x1FFFFFFF;
    private readonly List<int> canFilteredRows = new();
    private readonly CanSendScheduler canScheduler = new();
    private readonly List<CanSendTemplate> canTemplates = new();
    private Form? canPlanForm;
    private DataGridView? canPlanGrid;
    private Label? canPlanStatus;
    private sealed record CanLogEntry(DateTime HostTime, string Direction, CanFrame Frame);
    private readonly CanLogEntry?[] canRows = new CanLogEntry[CanVisibleLimit];
    private int canRowStart, canRowCount;
    private sealed class CanSummary { public required CanLogEntry Latest; public long Count; public double? CycleMs; }
    private readonly List<CanSummary> canSummaries = new();
    private readonly Dictionary<(uint Id, bool Extended, bool Remote, byte Kind), CanSummary> canSummaryLookup = new();
    private long canSummaryDiscarded;
    private bool CanSummaryMode => canViewInput.SelectedIndex == 0;
    private int CanViewCount => canFilteredRows.Count;
    private long canRxCount, canTxCount, canDiscardedCount;
    private DateTime nextCanStatusCheck;
    private const int CanVisibleLimit = 3000;

    private Control BuildCanPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = UiPalette.Surface, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(SectionTitle("CAN 报文收发与解析"), 0, 0);
        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, BackColor = UiPalette.Surface };
        static Label LabelFor(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(6, 9, 4, 0), ForeColor = UiPalette.Muted };
        static void StyleButton(Button button, string text, int width, bool primary = false)
        { button.Text = text; button.Width = width; button.Height = 30; button.Margin = new Padding(2, 3, 5, 0); UiPalette.StyleButton(button, primary); }
        canChannelInput.DropDownStyle = ComboBoxStyle.DropDownList; canChannelInput.Width = 200;
        canChannelInput.DisplayMember = nameof(CanChannel.Name); canChannelInput.Margin = new Padding(0, 4, 4, 0);
        canBaudInput.DropDownStyle = ComboBoxStyle.DropDownList; canBaudInput.Width = 116; canBaudInput.Margin = new Padding(0, 4, 4, 0);
        canBaudInput.Items.AddRange(new object[] { new CanBitrate("1 Mbit/s", 0x0014), new CanBitrate("800 kbit/s", 0x0016),
            new CanBitrate("500 kbit/s", 0x001C), new CanBitrate("250 kbit/s", 0x011C), new CanBitrate("125 kbit/s", 0x031C),
            new CanBitrate("100 kbit/s", 0x432F), new CanBitrate("50 kbit/s", 0x472F), new CanBitrate("20 kbit/s", 0x532F), new CanBitrate("10 kbit/s", 0x672F) });
        canBaudInput.SelectedIndex = 2;
        StyleButton(canRefreshButton, "刷新通道", 86); canRefreshButton.Click += (_, _) => RefreshCanChannels();
        StyleButton(canConnectButton, "连接 CAN", 94, true); canConnectButton.Click += (_, _) => ToggleCanConnection();
        canStatusLabel.AutoSize = true; canStatusLabel.Margin = new Padding(8, 9, 4, 0); canStatusLabel.ForeColor = UiPalette.Muted;
        canStatusLabel.Text = "未连接 · Classic CAN";
        settings.Controls.Add(LabelFor("适配器")); settings.Controls.Add(canChannelInput); settings.Controls.Add(canRefreshButton);
        canListenOnly.Text = "只监听"; canListenOnly.AutoSize = true; canListenOnly.Margin = new Padding(6, 8, 4, 0);
        settings.Controls.Add(LabelFor("波特率")); settings.Controls.Add(canBaudInput); settings.Controls.Add(canListenOnly); settings.Controls.Add(canConnectButton); settings.Controls.Add(canStatusLabel);
        root.Controls.Add(settings, 0, 1);

        var send = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, BackColor = UiPalette.Surface };
        canFrameTypeInput.DropDownStyle = ComboBoxStyle.DropDownList; canFrameTypeInput.Items.AddRange(new object[] { "标准帧 11 位", "扩展帧 29 位" });
        canFrameTypeInput.SelectedIndex = 0; canFrameTypeInput.Width = 120; canFrameTypeInput.Margin = new Padding(0, 5, 4, 0);
        canIdInput.Width = 95; canIdInput.Text = "321"; canIdInput.Margin = new Padding(0, 5, 4, 0);
        canDataInput.Width = 220; canDataInput.Text = "01 02 03 04 05 06 07 08"; canDataInput.Margin = new Padding(0, 5, 4, 0);
        canRemoteInput.Text = "远程帧"; canRemoteInput.AutoSize = true; canRemoteInput.Margin = new Padding(5, 8, 5, 0);
        canRemoteInput.CheckedChanged += (_, _) => { canDataInput.Enabled = !canRemoteInput.Checked; canRemoteDlcInput.Enabled = canRemoteInput.Checked; };
        canRemoteDlcInput.Minimum = 0; canRemoteDlcInput.Maximum = 8; canRemoteDlcInput.Value = 0; canRemoteDlcInput.Width = 46; canRemoteDlcInput.Enabled = false; canRemoteDlcInput.Margin = new Padding(0, 5, 4, 0);
        StyleButton(canSendButton, "发送一帧", 90, true); canSendButton.Enabled = false; canSendButton.Click += (_, _) => SendCanFrame();
        var clear = new Button(); StyleButton(clear, "清空", 64); clear.Click += (_, _) =>
        { Array.Clear(canRows); canRowStart = canRowCount = 0; canSummaries.Clear(); canSummaryLookup.Clear();
          canSummaryDiscarded = 0; canGrid.RowCount = 0; canTransmitGrid.Rows.Clear(); canDetails.Clear(); canDiscardedCount = canRxCount = canTxCount = 0; UpdateCanCounts(); };
        canFollowInput.Text = "跟随最新"; canFollowInput.Checked = true; canFollowInput.AutoSize = true; canFollowInput.Margin = new Padding(4, 8, 3, 0);
        var export = new Button(); StyleButton(export, "导出 CSV", 86); export.Click += (_, _) => ExportCanFrames();
        send.Controls.Add(LabelFor("类型")); send.Controls.Add(canFrameTypeInput); send.Controls.Add(LabelFor("ID (HEX)")); send.Controls.Add(canIdInput);
        send.Controls.Add(LabelFor("数据 (HEX)")); send.Controls.Add(canDataInput); send.Controls.Add(canRemoteInput); send.Controls.Add(LabelFor("DLC"));
        canViewInput.DropDownStyle = ComboBoxStyle.DropDownList; canViewInput.Width = 104; canViewInput.Margin = new Padding(4, 5, 0, 0);
        canViewInput.Items.AddRange(new object[] { "按 ID 汇总", "逐帧记录" }); canViewInput.SelectedIndex = 0;
        canViewInput.SelectedIndexChanged += (_, _) => RefreshCanDisplay();
        send.Controls.Add(canRemoteDlcInput); send.Controls.Add(canSendButton);
        root.Controls.Add(send, 0, 2);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, BackColor = UiPalette.Surface };
        canFilterEnabled.Text = "按 ID 过滤显示"; canFilterEnabled.AutoSize = true; canFilterEnabled.Margin = new Padding(5, 8, 4, 0);
        canFilterFrom.Width = canFilterTo.Width = 95; canFilterFrom.Text = "000"; canFilterTo.Text = "1FFFFFFF";
        canFilterFrom.Margin = canFilterTo.Margin = new Padding(0, 5, 4, 0);
        var applyFilter = new Button(); StyleButton(applyFilter, "应用过滤", 82); applyFilter.Click += (_, _) => ApplyCanFilter();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, BackColor = UiPalette.Surface };
        var plans = new Button(); StyleButton(plans, "发送计划", 90); plans.Click += (_, _) => ShowCanPlans();
        var stop = new Button(); StyleButton(stop, "停止周期发送", 110); stop.Click += (_, _) => StopAllCanPlans("已停止周期发送。");
        StyleButton(canTraceButton, "开始记录", 88); canTraceButton.Click += async (_, _) => await ToggleCanTraceAsync();
        canTraceStatus.AutoSize = true; canTraceStatus.Margin = new Padding(6, 9, 3, 0); canTraceStatus.ForeColor = UiPalette.Muted; canTraceStatus.Text = "未记录";
        tools.Controls.Add(canFilterEnabled); tools.Controls.Add(LabelFor("起始 HEX")); tools.Controls.Add(canFilterFrom);
        tools.Controls.Add(LabelFor("结束 HEX")); tools.Controls.Add(canFilterTo); tools.Controls.Add(applyFilter);
        tools.Controls.Add(canViewInput); tools.Controls.Add(canFollowInput); tools.Controls.Add(clear); tools.Controls.Add(export);
        root.Controls.Add(tools, 0, 3);
        actions.Controls.Add(plans); actions.Controls.Add(stop); actions.Controls.Add(canTraceButton); actions.Controls.Add(canTraceStatus);
        root.Controls.Add(actions, 0, 4);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, Panel1MinSize = 70, Panel2MinSize = 100 };
        canGrid.Dock = DockStyle.Fill; canGrid.ReadOnly = true; canGrid.VirtualMode = true; canGrid.AllowUserToAddRows = false; canGrid.AllowUserToDeleteRows = false;
        canGrid.RowHeadersVisible = false; canGrid.MultiSelect = false; canGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        canGrid.BackgroundColor = UiPalette.Surface; canGrid.BorderStyle = BorderStyle.None; ConfigureOperationalGridStyle(canGrid);
        canGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "time", HeaderText = "主机时间", Width = 115 });
        canGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "direction", HeaderText = "方向", Width = 80 });
        canGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = "ID (HEX)", Width = 110 });
        canGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "type", HeaderText = "类型", Width = 115 });
        canGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "dlc", HeaderText = "DLC", Width = 60 });
        canGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "data", HeaderText = "数据 (HEX)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        canGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "deviceTime", HeaderText = "设备计时 (µs)", Width = 135 });
        canGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "cycle", HeaderText = "周期 (ms)", Width = 112 });
        canGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "count", HeaderText = "次数", Width = 70 });
        canGrid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= CanViewCount) return;
            var entry = CanViewEntryAt(e.RowIndex);
            var frame = entry.Frame;
            e.Value = e.ColumnIndex switch
            {
                0 => entry.HostTime.ToString("HH:mm:ss.fff"), 1 => entry.Direction,
                2 => frame.Id.ToString(frame.Extended ? "X8" : "X3"), 3 => CanType(frame),
                4 => frame.Dlc, 5 => string.Join(" ", frame.Data.Select(value => value.ToString("X2"))),
                6 => frame.DeviceTimestampUs?.ToString(CultureInfo.InvariantCulture) ?? "--",
                7 => CanSummaryMode && canSummaries[canFilteredRows[e.RowIndex]].CycleMs is double cycle ? cycle.ToString("0.###", CultureInfo.InvariantCulture) : "--",
                8 => CanSummaryMode ? canSummaries[canFilteredRows[e.RowIndex]].Count.ToString(CultureInfo.InvariantCulture) : "--", _ => null
            };
        };
        canGrid.SelectionChanged += (_, _) =>
        {
            var row = canGrid.CurrentCell?.RowIndex ?? -1;
            canDetails.Text = row >= 0 && row < CanViewCount ? CanInterpreter.Explain(CanViewEntryAt(row).Frame) : "选择一条报文查看解释。";
        };
        split.Panel1.Controls.Add(canGrid);
        canDetails.Multiline = true; canDetails.ReadOnly = true; canDetails.ScrollBars = ScrollBars.Vertical; canDetails.Dock = DockStyle.Fill;
        canDetails.BorderStyle = BorderStyle.None; canDetails.BackColor = UiPalette.CodeBackground; canDetails.ForeColor = UiPalette.CodeText;
        canDetails.Text = "选择一条报文查看解释。";
        canCountLabel.Dock = DockStyle.Bottom; canCountLabel.Height = 26; canCountLabel.ForeColor = UiPalette.Muted; canCountLabel.TextAlign = ContentAlignment.MiddleLeft;
        var lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = UiPalette.Surface };
        lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52)); lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        lower.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        lower.Controls.Add(new Label { Text = "发送记录 · 双击载入输入框", Dock = DockStyle.Fill, ForeColor = UiPalette.Muted }, 0, 0);
        lower.Controls.Add(new Label { Text = "报文解释", Dock = DockStyle.Fill, ForeColor = UiPalette.Muted }, 1, 0);
        canTransmitGrid.Dock = DockStyle.Fill; canTransmitGrid.ReadOnly = true; canTransmitGrid.AllowUserToAddRows = false;
        canTransmitGrid.RowHeadersVisible = false; canTransmitGrid.MultiSelect = false; canTransmitGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        canTransmitGrid.BackgroundColor = UiPalette.Surface; canTransmitGrid.BorderStyle = BorderStyle.None; ConfigureOperationalGridStyle(canTransmitGrid);
        canTransmitGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "时间", Width = 98 });
        canTransmitGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID", Width = 90 });
        canTransmitGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "类型", Width = 80 });
        canTransmitGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "DLC", Width = 50 });
        canTransmitGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "数据 (HEX)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        canTransmitGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || canTransmitGrid.Rows[e.RowIndex].Tag is not CanFrame frame) return;
            canFrameTypeInput.SelectedIndex = frame.Extended ? 1 : 0;
            canIdInput.Text = frame.Id.ToString("X");
            canRemoteInput.Checked = frame.Remote;
            canRemoteDlcInput.Value = frame.Dlc;
            canDataInput.Text = string.Join(" ", frame.Data.Select(value => value.ToString("X2")));
        };
        lower.Controls.Add(canTransmitGrid, 0, 1); lower.Controls.Add(canDetails, 1, 1);
        split.Panel2.Controls.Add(lower); split.Panel2.Controls.Add(canCountLabel);
        root.Controls.Add(split, 0, 5);
        split.Resize += (_, _) => SetPreferredSplitterDistance(split, Math.Max(70, split.Height - Math.Clamp(split.Height / 2, 110, 165)));
        Shown += (_, _) => SetPreferredSplitterDistance(split, Math.Max(70, split.Height - Math.Clamp(split.Height / 2, 110, 165)));
        canTimer.Tick += (_, _) => DrainCanFrames();
        UpdateCanCounts();
        return root;
    }

    private void RefreshCanChannels()
    {
        if (canAdapter.IsOpen) return;
        try
        {
            var previous = (canChannelInput.SelectedItem as CanChannel)?.Handle;
            var channels = canAdapter.ListChannels();
            canChannelInput.Items.Clear();
            foreach (var channel in channels) canChannelInput.Items.Add(channel);
            var selected = channels.ToList().FindIndex(ch => ch.Handle == previous);
            canChannelInput.SelectedIndex = selected >= 0 ? selected : channels.Count > 0 ? 0 : -1;
            canStatusLabel.Text = channels.Count == 0 ? "未发现 PCAN-USB · 请插入并刷新" : $"发现 {channels.Count} 个 PCAN-USB 通道";
        }
        catch (Exception ex) { canStatusLabel.Text = "通道查询失败：" + ex.Message; }
    }
    private void ToggleCanConnection()
    {
        if (canAdapter.IsOpen) { CloseCanConnection(); return; }
        if (canChannelInput.SelectedItem is not CanChannel channel || canBaudInput.SelectedItem is not CanBitrate baud)
        { canStatusLabel.Text = "请刷新并选择 PCAN-USB 通道与波特率。"; return; }
        try
        {
            canAdapter.Open(channel.Handle, baud.Code, canListenOnly.Checked);
            // Adapter-relative clocks can restart; do not calculate a cycle across connections.
            canSummaries.Clear(); canSummaryLookup.Clear(); canSummaryDiscarded = 0; RefreshCanDisplay();
            canConnectButton.Text = "断开 CAN"; canSendButton.Enabled = !canAdapter.IsListenOnly; canChannelInput.Enabled = canBaudInput.Enabled = canRefreshButton.Enabled = canListenOnly.Enabled = false;
            canStatusLabel.Text = $"已连接 {channel.Name} · {baud.Name} · {(canAdapter.IsListenOnly ? "只监听" : "正常模式")} · Classic CAN";
            nextCanStatusCheck = DateTime.UtcNow.AddSeconds(1); canTimer.Start();
        }
        catch (Exception ex) { canStatusLabel.Text = "连接失败：" + ex.Message; }
    }
    private void CloseCanConnection()
    {
        canTimer.Stop();
        StopAllCanPlans("CAN 已断开，周期发送已停止。");
        try { canAdapter.Close(); canStatusLabel.Text = "CAN 已断开"; }
        catch (Exception ex) { canStatusLabel.Text = "断开时出错：" + ex.Message; }
        canConnectButton.Text = "连接 CAN"; canSendButton.Enabled = false; canChannelInput.Enabled = canBaudInput.Enabled = canRefreshButton.Enabled = canListenOnly.Enabled = true;
    }
    private void DrainCanFrames()
    {
        if (!canAdapter.IsOpen) { canTimer.Stop(); return; }
        try
        {
            for (var count = 0; count < 256; count++)
            {
                var frame = canAdapter.Read();
                if (frame is null) break;
                canRxCount++; AddCanRow("RX", frame);
                if ((frame.DriverFlags & 0x80) != 0 && (frame.DriverStatus & 0x10) != 0)
                    StopAllCanPlans("总线 Bus-off，周期发送已停止；请检查总线后重新连接。");
            }
            if (DateTime.UtcNow >= nextCanStatusCheck)
            {
                var status = canAdapter.BusStatus();
                canStatusLabel.Text = $"CAN 总线：{status}";
                nextCanStatusCheck = DateTime.UtcNow.AddSeconds(1);
            }
            TickCanPlans();
            if (canTrace is not null && !canTraceFinishing && canTrace.Failure is null)
                canTraceStatus.Text = $"正在记录 {canTraceCount} 帧";
            if (canTrace is { Failure: not null } && !canTraceFinishing) _ = StopCanTraceAsync("记录写入失败");
            RefreshCanDisplay();
        }
        catch (Exception ex) { CloseCanConnection(); canStatusLabel.Text = "CAN 接收停止：" + ex.Message; }
    }
    private void SendCanFrame()
    {
        try
        {
            var extended = canFrameTypeInput.SelectedIndex == 1;
            var id = CanInterpreter.ParseId(canIdInput.Text, extended);
            var bytes = canRemoteInput.Checked ? Array.Empty<byte>() : CanInterpreter.ParseHex(canDataInput.Text);
            var frame = new CanFrame(id, extended, canRemoteInput.Checked, (byte)(canRemoteInput.Checked ? canRemoteDlcInput.Value : bytes.Length), bytes);
            QueueCanFrame(frame);
            RefreshCanDisplay();
            canStatusLabel.Text = "已提交发送队列；不代表对端已处理。";
        }
        catch (Exception ex) { canStatusLabel.Text = "发送失败：" + ex.Message; }
    }
    private void QueueCanFrame(CanFrame frame)
    {
        canAdapter.Write(frame);
        canTxCount++; AddCanRow("TX", frame);
        if (canTransmitGrid.Rows.Count >= 200) canTransmitGrid.Rows.RemoveAt(0);
        var row = canTransmitGrid.Rows.Add(DateTime.Now.ToString("HH:mm:ss.fff"), frame.Id.ToString("X"), CanType(frame), frame.Dlc,
            string.Join(" ", frame.Data.Select(value => value.ToString("X2"))));
        canTransmitGrid.Rows[row].Tag = frame;
    }
    private CanFrame CanFrameFromInputs()
    {
        var extended = canFrameTypeInput.SelectedIndex == 1;
        var id = CanInterpreter.ParseId(canIdInput.Text, extended);
        var bytes = canRemoteInput.Checked ? Array.Empty<byte>() : CanInterpreter.ParseHex(canDataInput.Text);
        return new CanFrame(id, extended, canRemoteInput.Checked, (byte)(canRemoteInput.Checked ? canRemoteDlcInput.Value : bytes.Length), bytes);
    }
    private static string CanType(CanFrame frame) => (frame.DriverFlags & 0x80) != 0 ? "状态消息" :
        (frame.DriverFlags & 0x40) != 0 ? "错误帧" : ((frame.DriverFlags & 0x20) != 0 ? "回显 " : "") + (frame.Extended ? "扩展" : "标准") + (frame.Remote ? "远程帧" : "数据帧");
    private CanLogEntry CanEntryAt(int index) => canRows[(canRowStart + index) % CanVisibleLimit]!;
    private CanLogEntry CanViewEntryAt(int index) => CanSummaryMode ? canSummaries[canFilteredRows[index]].Latest : CanEntryAt(canFilteredRows[index]);
    private void AddCanRow(string direction, CanFrame frame)
    {
        var entry = new CanLogEntry(DateTime.Now, direction, frame);
        if (canTrace is not null && !canTraceFinishing)
        {
            if (canTrace.Append(entry.HostTime, direction, frame)) canTraceCount++;
            else _ = StopCanTraceAsync("记录异常，部分报文未写入");
        }
        if (direction == "RX")
        {
            var key = (frame.Id, frame.Extended, frame.Remote, (byte)(frame.DriverFlags & 0xE0));
            if (canSummaryLookup.TryGetValue(key, out var summary))
            {
                var previous = summary.Latest.Frame.DeviceTimestampUs;
                summary.CycleMs = previous is ulong oldTime && frame.DeviceTimestampUs is ulong newTime && newTime >= oldTime
                    ? (newTime - oldTime) / 1000.0 : null;
                summary.Latest = entry;
                summary.Count++;
            }
            else if (canSummaries.Count < CanVisibleLimit)
            {
                var created = new CanSummary { Latest = entry, Count = 1 };
                canSummaryLookup.Add(key, created); canSummaries.Add(created);
            }
            else canSummaryDiscarded++;
        }
        if (canRowCount == CanVisibleLimit)
        {
            canRows[canRowStart] = entry;
            canRowStart = (canRowStart + 1) % CanVisibleLimit;
            canDiscardedCount++;
        }
        else
        {
            canRows[(canRowStart + canRowCount) % CanVisibleLimit] = entry;
            canRowCount++;
        }
    }
    private void RefreshCanDisplay()
    {
        canFilteredRows.Clear();
        var count = CanSummaryMode ? canSummaries.Count : canRowCount;
        for (var index = 0; index < count; index++)
        {
            var frame = CanSummaryMode ? canSummaries[index].Latest.Frame : CanEntryAt(index).Frame;
            if (!canFilterEnabled.Checked || (frame.DriverFlags & 0xC0) != 0 || frame.Id >= canFilterLow && frame.Id <= canFilterHigh)
                canFilteredRows.Add(index);
        }
        canGrid.RowCount = CanViewCount;
        canGrid.Invalidate();
        if (!CanSummaryMode && canFollowInput.Checked && CanViewCount > 0 && canGrid.DisplayedRowCount(false) > 0)
            canGrid.FirstDisplayedScrollingRowIndex = Math.Max(0, CanViewCount - canGrid.DisplayedRowCount(false));
        var row = canGrid.CurrentCell?.RowIndex ?? -1;
        canDetails.Text = row >= 0 && row < CanViewCount ? CanInterpreter.Explain(CanViewEntryAt(row).Frame) : "选择一条报文查看解释。";
        UpdateCanCounts();
    }
    private void UpdateCanCounts() => canCountLabel.Text = $"RX {canRxCount} · TX入队 {canTxCount} · 保留 {canRowCount}/{CanVisibleLimit} 帧 · 已移除较早记录 {canDiscardedCount}" +
        (canSummaryDiscarded > 0 ? $" · 未汇总帧 {canSummaryDiscarded}" : "");
    private void ApplyCanFilter()
    {
        try
        {
            if (canFilterEnabled.Checked)
            {
                var low = CanInterpreter.ParseId(canFilterFrom.Text, true);
                var high = CanInterpreter.ParseId(canFilterTo.Text, true);
                if (low > high) throw new ArgumentException("起始 ID 不能大于结束 ID。");
                canFilterLow = low; canFilterHigh = high;
            }
            RefreshCanDisplay();
            canStatusLabel.Text = canFilterEnabled.Checked ? "已过滤显示；原始记录仍保留并参与导出。" : "已显示全部报文。";
        }
        catch (Exception ex) { canFilterEnabled.Checked = false; RefreshCanDisplay(); canStatusLabel.Text = "过滤设置无效：" + ex.Message; }
    }
    private void ExportCanFrames()
    {
        using var dialog = new SaveFileDialog { Title = "导出当前 CAN 报文", Filter = "CSV 文件 (*.csv)|*.csv", FileName = $"CAN_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
            writer.WriteLine("HostTime,Direction,ID_Hex,Type,DLC,Data_Hex,DeviceElapsedUs");
            static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
            for (var index = 0; index < canRowCount; index++)
            {
                var entry = CanEntryAt(index); var frame = entry.Frame;
                writer.WriteLine(string.Join(",", new[] { entry.HostTime.ToString("yyyy-MM-dd HH:mm:ss.fff"), entry.Direction,
                    frame.Id.ToString(frame.Extended ? "X8" : "X3"), CanType(frame), frame.Dlc.ToString(CultureInfo.InvariantCulture),
                    string.Join(" ", frame.Data.Select(value => value.ToString("X2"))), frame.DeviceTimestampUs?.ToString(CultureInfo.InvariantCulture) ?? "" }.Select(Csv)));
            }
            canStatusLabel.Text = $"已导出 {canRowCount} 帧（仅当前保留记录）";
        }
        catch (Exception ex) { canStatusLabel.Text = "导出失败：" + ex.Message; }
    }
}
