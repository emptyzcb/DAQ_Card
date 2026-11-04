using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private readonly SerialConnection serialConnection = new();
    private readonly ComboBox serialPortInput = new();
    private readonly ComboBox serialBaudInput = new();
    private readonly ComboBox serialDataBitsInput = new();
    private readonly ComboBox serialParityInput = new();
    private readonly ComboBox serialStopBitsInput = new();
    private readonly ComboBox serialHandshakeInput = new();
    private readonly CheckBox serialDtrInput = new();
    private readonly CheckBox serialRtsInput = new();
    private readonly ComboBox serialEndingInput = new();
    private readonly Button serialConnectButton = new();
    private readonly Button serialSendButton = new();
    private readonly TextBox serialSendInput = new();
    private readonly CheckBox serialHexInput = new();
    private readonly CheckBox serialDisplayHexInput = new();
    private readonly Label serialStatusLabel = new();
    private readonly RichTextBox serialTrafficBox = new();
    private readonly SerialWaveformCapture serialWaveformCapture = new();
    private readonly TrendChartControl serialWaveformChart = new();
    private readonly SplitContainer serialChartLogSplit = new();
    private readonly ComboBox serialWaveformModeInput = new();
    private readonly ComboBox serialWindowInput = new();
    private readonly FlowLayoutPanel serialChannelPanel = new();
    private readonly Label serialChannelEmptyLabel = new();
    private readonly List<Panel> serialChannelRows = new();
    private readonly List<CheckBox> serialChannelChecks = new();
    private readonly List<Label> serialChannelValues = new();
    private readonly Label serialSampleStatus = new();
    private readonly Button serialPauseButton = new();
    private bool serialWaveformPaused;
    private DateTime? serialFrozenEnd;
    private static readonly Color[] SerialChannelColors =
    {
        Color.FromArgb(247, 25, 124), Color.FromArgb(19, 146, 73),
        Color.FromArgb(42, 83, 243), Color.FromArgb(238, 146, 34),
        Color.FromArgb(139, 74, 204), Color.FromArgb(25, 159, 179),
        Color.FromArgb(204, 70, 70), Color.FromArgb(102, 114, 129)
    };
    private readonly System.Windows.Forms.Timer serialTrafficTimer = new() { Interval = 100 };
    private readonly object serialTrafficLock = new();
    private readonly Queue<(DateTime Time, string Direction, byte[] Data, string? Message)> serialTraffic = new();
    private bool serialOpening;

    private void InitializeSerialFeatures()
    {
        serialConnection.Received += OnSerialReceived;
        serialConnection.Error += ex => QueueSerialTraffic("ERR", Array.Empty<byte>(), ex.Message);
        serialTrafficTimer.Tick += (_, _) => FlushSerialTraffic();
        serialTrafficTimer.Start();
        UpdateSerialUi();
    }

    private Control BuildSerialPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3,
            RowCount = 1, BackColor = UiPalette.Surface };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));

        var settings = new FlowLayoutPanel { Name = "serialSettingsPanel", Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(6, 9, 0, 12),
            BackColor = Color.FromArgb(247, 249, 251) };
        settings.Controls.Add(SerialSectionLabel("协议与连接"));
        serialPortInput.DropDownStyle = ComboBoxStyle.DropDownList;
        serialPortInput.Width = 110;
        serialPortInput.DropDown += (_, _) => RefreshSerialPorts();
        settings.Controls.Add(SerialSettingRow("端口号", serialPortInput));
        var refresh = new Button { Text = "刷新端口", Width = 194, Height = 30, Margin = new Padding(0, 2, 0, 10) };
        UiPalette.StyleButton(refresh);
        refresh.Click += (_, _) => RefreshSerialPorts();
        settings.Controls.Add(refresh);
        settings.Controls.Add(SerialSectionLabel("串口参数"));
        serialBaudInput.DropDownStyle = ComboBoxStyle.DropDown;
        serialBaudInput.Width = 110;
        serialBaudInput.Items.AddRange(new object[]
        {
            "9600", "19200", "38400", "57600", "115200", "230400", "460800",
            "921600", "1000000", "1500000", "2000000", "3000000"
        });
        serialBaudInput.Text = "115200";
        settings.Controls.Add(SerialSettingRow("波特率", serialBaudInput));
        SetupSerialChoice(serialDataBitsInput, 110, "8", "5", "6", "7", "8");
        settings.Controls.Add(SerialSettingRow("数据位", serialDataBitsInput));
        SetupSerialChoice(serialParityInput, 110, "无", "无", "偶", "奇");
        settings.Controls.Add(SerialSettingRow("校验位", serialParityInput));
        SetupSerialChoice(serialStopBitsInput, 110, "1", "1", "1.5", "2");
        settings.Controls.Add(SerialSettingRow("停止位", serialStopBitsInput));
        SetupSerialChoice(serialHandshakeInput, 110, "无", "无", "XON/XOFF", "RTS/CTS");
        serialHandshakeInput.SelectedIndexChanged += (_, _) =>
            serialRtsInput.Enabled = serialConnection.IsOpen && serialHandshakeInput.Text != "RTS/CTS";
        settings.Controls.Add(SerialSettingRow("流控", serialHandshakeInput));
        var signalRow = new FlowLayoutPanel { Width = 196, Height = 30, WrapContents = false,
            Margin = new Padding(0, 4, 0, 7) };
        serialDtrInput.Text = "DTR";
        serialDtrInput.Width = 55;
        serialDtrInput.CheckedChanged += (_, _) => SetSerialLine(() => serialConnection.SetDtr(serialDtrInput.Checked));
        signalRow.Controls.Add(serialDtrInput);
        serialRtsInput.Text = "RTS";
        serialRtsInput.Width = 55;
        serialRtsInput.CheckedChanged += (_, _) => SetSerialLine(() => serialConnection.SetRts(serialRtsInput.Checked));
        signalRow.Controls.Add(serialRtsInput);
        var breakButton = new Button { Text = "Break", Width = 65, Height = 27 };
        UiPalette.StyleButton(breakButton);
        breakButton.Click += async (_, _) =>
        {
            if (!serialConnection.IsOpen) return;
            try { await Task.Run(() => serialConnection.SendBreak()); }
            catch (Exception ex) { QueueSerialTraffic("ERR", Array.Empty<byte>(), ex.Message); }
        };
        signalRow.Controls.Add(breakButton);
        settings.Controls.Add(signalRow);
        settings.Controls.Add(SerialSectionLabel("波形解析"));
        SetupSerialChoice(serialWaveformModeInput, 110, "数值文本", "数值文本", "JustFloat", "原始数据");
        var formatHint = new Label { Text = "每行最多 32 个数值\n逗号、空格或分号分隔",
            Width = 210, Height = 46, ForeColor = UiPalette.Muted,
            Font = new Font(Font.FontFamily, 9F), Margin = new Padding(0, 1, 0, 10) };
        serialWaveformModeInput.SelectedIndexChanged += (_, _) =>
        {
            serialWaveformCapture.Reset();
            formatHint.Text = serialWaveformModeInput.SelectedIndex switch
            {
                1 => "JustFloat：小端 float 数组\n帧尾 00 00 80 7F",
                2 => "原始字节只显示在收发记录中",
                _ => "每行最多 32 个数值\n逗号、空格或分号分隔"
            };
            RefreshSerialWaveform();
        };
        settings.Controls.Add(SerialSettingRow("数据格式", serialWaveformModeInput));
        settings.Controls.Add(formatHint);
        serialConnectButton.Text = "打开串口";
        serialConnectButton.Size = new Size(194, 34);
        serialConnectButton.Margin = Padding.Empty;
        UiPalette.StyleButton(serialConnectButton, primary: true);
        serialConnectButton.Click += async (_, _) => await ToggleSerialConnectionAsync();
        settings.Controls.Add(serialConnectButton);
        serialStatusLabel.Width = 194;
        serialStatusLabel.Height = 74;
        serialStatusLabel.ForeColor = UiPalette.Muted;
        serialStatusLabel.Margin = new Padding(0, 11, 0, 0);
        settings.Controls.Add(serialStatusLabel);
        root.Controls.Add(settings, 0, 0);

        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = 2, BackColor = UiPalette.Surface };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        serialChartLogSplit.Dock = DockStyle.Fill;
        serialChartLogSplit.Orientation = Orientation.Horizontal;
        serialChartLogSplit.BackColor = Color.FromArgb(221, 226, 232);
        serialChartLogSplit.SplitterWidth = 5;
        serialChartLogSplit.Panel1MinSize = 170;
        serialChartLogSplit.Panel2MinSize = 110;
        Shown += (_, _) =>
        {
            if (serialChartLogSplit.Height <= 285) return;
            serialChartLogSplit.SplitterDistance = Math.Clamp((int)(serialChartLogSplit.Height * 0.63),
                serialChartLogSplit.Panel1MinSize,
                serialChartLogSplit.Height - serialChartLogSplit.Panel2MinSize - serialChartLogSplit.SplitterWidth);
        };
        var chartArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = 2, BackColor = UiPalette.Surface };
        chartArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        chartArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var chartToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            AutoScroll = true, Padding = new Padding(9, 4, 4, 2),
            BackColor = Color.FromArgb(239, 242, 246) };
        var chartTab = SerialLabel("串口波形", 76);
        chartTab.ForeColor = UiPalette.Blue;
        chartTab.Font = new Font(Font, FontStyle.Bold);
        chartTab.BackColor = UiPalette.Surface;
        chartToolbar.Controls.Add(chartTab);
        chartToolbar.Controls.Add(SerialLabel("窗口", 38));
        SetupSerialChoice(serialWindowInput, 86, "30 秒", "10 秒", "30 秒", "2 分钟");
        serialWindowInput.SelectedIndexChanged += (_, _) => RefreshSerialWaveform();
        chartToolbar.Controls.Add(serialWindowInput);
        serialPauseButton.Text = "暂停";
        serialPauseButton.Size = new Size(57, 29);
        UiPalette.StyleButton(serialPauseButton);
        serialPauseButton.Click += (_, _) =>
        {
            serialWaveformPaused = !serialWaveformPaused;
            serialFrozenEnd = serialWaveformPaused ? DateTime.Now : null;
            serialPauseButton.Text = serialWaveformPaused ? "继续" : "暂停";
            if (!serialWaveformPaused) RefreshSerialWaveform();
        };
        chartToolbar.Controls.Add(serialPauseButton);
        var clearWaveform = new Button { Text = "清空波形", Size = new Size(82, 29) };
        UiPalette.StyleButton(clearWaveform);
        clearWaveform.Click += (_, _) => { serialWaveformCapture.Reset(); RefreshSerialWaveform(); };
        chartToolbar.Controls.Add(clearWaveform);
        chartArea.Controls.Add(chartToolbar, 0, 0);
        serialWaveformChart.Dock = DockStyle.Fill;
        serialWaveformChart.TimeZoomRequested += factor =>
        {
            serialWindowInput.SelectedIndex = factor < 1
                ? Math.Max(0, serialWindowInput.SelectedIndex - 1)
                : Math.Min(serialWindowInput.Items.Count - 1, serialWindowInput.SelectedIndex + 1);
        };
        chartArea.Controls.Add(serialWaveformChart, 0, 1);
        serialChartLogSplit.Panel1.Controls.Add(chartArea);
        var logArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = 2, BackColor = UiPalette.Surface };
        logArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        logArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
            Padding = new Padding(9, 4, 4, 1), BackColor = Color.FromArgb(247, 249, 251) };
        logToolbar.Controls.Add(SerialLabel("收发记录", 72));
        var clearLog = new Button { Text = "清空", Size = new Size(57, 28) };
        UiPalette.StyleButton(clearLog);
        clearLog.Click += (_, _) => serialTrafficBox.Clear();
        logToolbar.Controls.Add(clearLog);
        serialDisplayHexInput.Text = "HEX 显示";
        serialDisplayHexInput.Checked = false;
        serialDisplayHexInput.AutoSize = true;
        serialDisplayHexInput.Margin = new Padding(12, 7, 0, 0);
        logToolbar.Controls.Add(serialDisplayHexInput);
        serialSampleStatus.AutoSize = true;
        serialSampleStatus.Margin = new Padding(14, 5, 0, 0);
        serialSampleStatus.ForeColor = UiPalette.Muted;
        logToolbar.Controls.Add(serialSampleStatus);
        logArea.Controls.Add(logToolbar, 0, 0);
        serialTrafficBox.Dock = DockStyle.Fill;
        serialTrafficBox.ReadOnly = true;
        serialTrafficBox.WordWrap = false;
        serialTrafficBox.BorderStyle = BorderStyle.None;
        serialTrafficBox.BackColor = UiPalette.Surface;
        serialTrafficBox.ForeColor = UiPalette.Ink;
        serialTrafficBox.Font = new Font("Cascadia Mono", 9F);
        logArea.Controls.Add(serialTrafficBox, 0, 1);
        serialChartLogSplit.Panel2.Controls.Add(logArea);
        center.Controls.Add(serialChartLogSplit, 0, 0);

        var sendBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            Padding = new Padding(5, 7, 5, 5),
            BackColor = UiPalette.Surface
        };
        sendBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sendBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        sendBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 87));
        sendBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        sendBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        serialSendInput.Dock = DockStyle.Fill;
        serialSendInput.PlaceholderText = "输入文本；勾选 HEX 后输入如 01 03 00 00 00 01 84 0A";
        serialSendInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            _ = SendSerialTextAsync();
        };
        sendBar.Controls.Add(serialSendInput, 0, 0);
        serialHexInput.Text = "HEX";
        serialHexInput.TextAlign = ContentAlignment.MiddleCenter;
        serialHexInput.Dock = DockStyle.Fill;
        sendBar.Controls.Add(serialHexInput, 1, 0);
        SetupSerialChoice(serialEndingInput, 78, "无结束符", "无结束符", "CR", "LF", "CRLF");
        serialEndingInput.Dock = DockStyle.Fill;
        sendBar.Controls.Add(serialEndingInput, 2, 0);
        serialSendButton.Text = "发送";
        serialSendButton.Dock = DockStyle.Fill;
        UiPalette.StyleButton(serialSendButton, primary: true);
        serialSendButton.Click += async (_, _) => await SendSerialTextAsync();
        sendBar.Controls.Add(serialSendButton, 3, 0);
        var testButton = new Button { Text = "测试", Dock = DockStyle.Fill };
        UiPalette.StyleButton(testButton);
        testButton.Click += (_, _) => contentTabs.SelectedIndex = 5;
        sendBar.Controls.Add(testButton, 4, 0);
        center.Controls.Add(sendBar, 0, 1);
        root.Controls.Add(center, 1, 0);

        serialChannelPanel.Dock = DockStyle.Fill;
        serialChannelPanel.FlowDirection = FlowDirection.TopDown;
        serialChannelPanel.WrapContents = false;
        serialChannelPanel.AutoScroll = true;
        serialChannelPanel.BackColor = Color.FromArgb(250, 251, 253);
        serialChannelPanel.Padding = new Padding(9, 9, 0, 0);
        var channelTitle = SerialSectionLabel("波形通道");
        channelTitle.Width = 130;
        serialChannelPanel.Controls.Add(channelTitle);
        serialChannelEmptyLabel.Text = "等待数值帧";
        serialChannelEmptyLabel.Width = 130;
        serialChannelEmptyLabel.Height = 27;
        serialChannelEmptyLabel.ForeColor = UiPalette.Muted;
        serialChannelPanel.Controls.Add(serialChannelEmptyLabel);
        root.Controls.Add(serialChannelPanel, 2, 0);

        RefreshSerialPorts();
        RefreshSerialWaveform();
        return root;
    }

    private void RefreshSerialWaveform()
    {
        if (serialWaveformChart.IsDisposed) return;
        SyncSerialChannels();
        var seconds = serialWindowInput.SelectedIndex switch { 0 => 10, 2 => 120, _ => 30 };
        var window = TimeSpan.FromSeconds(seconds);
        var series = new List<TrendSeries>();
        for (var index = 0; index < serialWaveformCapture.ChannelCount; index++)
        {
            var points = serialWaveformCapture.GetPoints(index);
            serialChannelValues[index].Text = points.Count == 0 || !points[^1].IsValid
                ? "--" : points[^1].Value.ToString("0.######");
            if (serialChannelChecks[index].Checked)
                series.Add(new TrendSeries(new TrendChannel($"SER:{index}", "串口", $"CH{index + 1}", ""),
                    points, SerialChannelColors[index % SerialChannelColors.Length]));
        }
        serialWaveformChart.SetSeries(series, window, serialFrozenEnd ?? DateTime.Now,
            window, "串口数值");
        serialSampleStatus.Text = $"已采样 {serialWaveformCapture.SampleCount} 行 · {serialWaveformCapture.ChannelCount} 通道";
    }

    private void SyncSerialChannels()
    {
        var count = serialWaveformCapture.ChannelCount;
        while (serialChannelRows.Count > count)
        {
            var last = serialChannelRows.Count - 1;
            serialChannelPanel.Controls.Remove(serialChannelRows[last]);
            serialChannelRows[last].Dispose();
            serialChannelRows.RemoveAt(last);
            serialChannelChecks.RemoveAt(last);
            serialChannelValues.RemoveAt(last);
        }
        while (serialChannelRows.Count < count)
        {
            var index = serialChannelRows.Count;
            var color = SerialChannelColors[index % SerialChannelColors.Length];
            var row = new Panel { Width = 130, Height = 50 };
            var check = new CheckBox { Text = $"CH{index + 1}", Checked = true,
                Width = 108, Height = 24, Location = new Point(0, 1), ForeColor = color };
            check.CheckedChanged += (_, _) => RefreshSerialWaveform();
            row.Controls.Add(check);
            var value = new Label { Text = "--", Width = 125, Height = 19,
                Location = new Point(5, 27), ForeColor = color,
                Font = new Font(Font, FontStyle.Bold) };
            row.Controls.Add(value);
            serialChannelPanel.Controls.Add(row);
            serialChannelRows.Add(row);
            serialChannelChecks.Add(check);
            serialChannelValues.Add(value);
        }
        serialChannelEmptyLabel.Visible = count == 0;
    }

    private static Label SerialSectionLabel(string text) => new()
    {
        Text = text, Width = 194, Height = 32,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        ForeColor = UiPalette.Blue, TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 0, 0, 3)
    };

    private static Control SerialSettingRow(string label, Control input)
    {
        var row = new Panel { Width = 196, Height = 36, Margin = Padding.Empty };
        row.Controls.Add(new Label { Text = label, Width = 76, Height = 29,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiPalette.Ink,
            Location = new Point(0, 2) });
        input.Location = new Point(82, 2);
        input.Height = 29;
        row.Controls.Add(input);
        return row;
    }

    private static Label SerialLabel(string text, int width) => new()
    {
        Text = text, Width = width, Height = 29, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = UiPalette.Muted, Margin = new Padding(7, 0, 2, 0)
    };

    private static void SetupSerialChoice(ComboBox combo, int width, string selected, params string[] values)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Width = width;
        combo.Items.AddRange(values.Cast<object>().ToArray());
        combo.SelectedItem = selected;
    }

    private void SetSerialLine(Action update)
    {
        if (!serialConnection.IsOpen) return;
        try { update(); }
        catch (Exception ex) { QueueSerialTraffic("ERR", Array.Empty<byte>(), ex.Message); }
    }

    private void RefreshSerialPorts()
    {
        var selected = serialPortInput.Text;
        var ports = SerialPort.GetPortNames().OrderBy(name =>
            int.TryParse(name.AsSpan(3), out var number) ? number : int.MaxValue).ToArray();
        serialPortInput.Items.Clear();
        serialPortInput.Items.AddRange(ports.Cast<object>().ToArray());
        serialPortInput.SelectedItem = ports.Contains(selected) ? selected : ports.FirstOrDefault();
        if (ports.Length == 0) serialStatusLabel.Text = "没有检测到串口。连接串口设备后点刷新。";
    }

    private async Task ToggleSerialConnectionAsync()
    {
        if (serialOpening) return;
        if (serialConnection.IsOpen)
        {
            serialConnection.Close();
            QueueSerialTraffic("SYS", Array.Empty<byte>(), "串口已关闭");
            UpdateSerialUi();
            return;
        }
        if (serialPortInput.SelectedItem is not string portName)
        {
            serialStatusLabel.Text = "请先选择串口。";
            return;
        }
        if (!int.TryParse(serialBaudInput.Text, out var baud) || baud is < 300 or > 4000000)
        {
            serialStatusLabel.Text = "波特率请输入 300–4000000；实际最高速率取决于适配器和驱动。";
            return;
        }
        var parity = serialParityInput.Text switch
        {
            "偶" => Parity.Even,
            "奇" => Parity.Odd,
            _ => Parity.None
        };
        var stopBits = serialStopBitsInput.Text switch
        {
            "1.5" => StopBits.OnePointFive,
            "2" => StopBits.Two,
            _ => StopBits.One
        };
        var options = new SerialConnectionOptions(portName, baud,
            int.Parse(serialDataBitsInput.Text), parity, stopBits,
            serialHandshakeInput.Text switch
            {
                "XON/XOFF" => Handshake.XOnXOff,
                "RTS/CTS" => Handshake.RequestToSend,
                _ => Handshake.None
            });
        serialOpening = true;
        UpdateSerialUi();
        try
        {
            await Task.Run(() => serialConnection.Open(options));
            if (serialDtrInput.Checked) serialConnection.SetDtr(true);
            if (serialRtsInput.Checked && options.Handshake != Handshake.RequestToSend)
                serialConnection.SetRts(true);
            QueueSerialTraffic("SYS", Array.Empty<byte>(),
                $"{portName} 已打开 · {baud} bps · {options.DataBits}{serialParityInput.Text}/{serialStopBitsInput.Text}");
        }
        catch (Exception ex)
        {
            QueueSerialTraffic("ERR", Array.Empty<byte>(), $"打开串口失败：{ex.Message}");
            serialStatusLabel.Text = $"打开失败：{ex.Message}";
        }
        finally
        {
            serialOpening = false;
            UpdateSerialUi();
        }
    }

    private void UpdateSerialUi()
    {
        var opened = serialConnection.IsOpen;
        serialConnectButton.Text = serialOpening ? "正在打开" : opened ? "关闭串口" : "打开串口";
        serialConnectButton.Enabled = !serialOpening;
        serialConnectButton.BackColor = opened ? UiPalette.Red : UiPalette.Blue;
        serialPortInput.Enabled = !opened && !serialOpening;
        serialBaudInput.Enabled = !opened && !serialOpening;
        serialDataBitsInput.Enabled = !opened && !serialOpening;
        serialParityInput.Enabled = !opened && !serialOpening;
        serialStopBitsInput.Enabled = !opened && !serialOpening;
        serialHandshakeInput.Enabled = !opened && !serialOpening;
        serialDtrInput.Enabled = opened;
        serialRtsInput.Enabled = opened && serialHandshakeInput.Text != "RTS/CTS";
        serialSendButton.Enabled = opened;
        serialStatusLabel.Text = opened
            ? $"{serialConnection.PortName} 已连接 · 串口与以太网可同时工作"
            : "串口未连接 · 选择端口后打开";
        UpdateTestSerialStatus();
    }

    private async Task SendSerialTextAsync()
    {
        if (!serialConnection.IsOpen) return;
        try
        {
            byte[] data;
            if (serialHexInput.Checked)
            {
                data = ParseHexBytes(serialSendInput.Text);
            }
            else
            {
                var ending = serialEndingInput.Text switch
                {
                    "CR" => "\r",
                    "LF" => "\n",
                    "CRLF" => "\r\n",
                    _ => string.Empty
                };
                data = Encoding.UTF8.GetBytes(serialSendInput.Text + ending);
            }
            if (data.Length == 0) return;
            await Task.Run(() => serialConnection.Send(data));
            QueueSerialTraffic("TX", data);
        }
        catch (Exception ex)
        {
            QueueSerialTraffic("ERR", Array.Empty<byte>(), $"发送失败：{ex.Message}");
        }
    }

    internal static byte[] ParseHexBytes(string text)
    {
        var compact = Regex.Replace(text, @"(?i)0x|[\s,;:_-]", string.Empty);
        if (compact.Length == 0) return Array.Empty<byte>();
        if ((compact.Length & 1) != 0 || !compact.All(Uri.IsHexDigit))
            throw new FormatException("HEX 帧必须由完整的两位十六进制字节组成。");
        return Convert.FromHexString(compact);
    }

    private void OnSerialReceived(byte[] bytes) => QueueSerialTraffic("RX", bytes);

    private void QueueSerialTraffic(string direction, byte[] data, string? message = null)
    {
        lock (serialTrafficLock)
        {
            if (serialTraffic.Count >= 3000) serialTraffic.Dequeue();
            serialTraffic.Enqueue((DateTime.Now, direction, data, message));
        }
    }

    private void FlushSerialTraffic()
    {
        var batch = new List<(DateTime Time, string Direction, byte[] Data, string? Message)>();
        lock (serialTrafficLock)
        {
            while (serialTraffic.Count > 0 && batch.Count < 100) batch.Add(serialTraffic.Dequeue());
        }
        foreach (var entry in batch)
        {
            if (entry.Direction == "RX")
            {
                if (serialWaveformModeInput.SelectedIndex == 0)
                    serialWaveformCapture.Append(entry.Data, entry.Time);
                else if (serialWaveformModeInput.SelectedIndex == 1)
                    serialWaveformCapture.AppendJustFloat(entry.Data, entry.Time);
            }
            var content = entry.Message ?? (serialDisplayHexInput.Checked
                ? BitConverter.ToString(entry.Data).Replace('-', ' ')
                : Encoding.UTF8.GetString(entry.Data).Replace("\r", "\\r").Replace("\n", "\\n"));
            serialTrafficBox.SelectionStart = serialTrafficBox.TextLength;
            serialTrafficBox.SelectionColor = entry.Direction switch
            {
                "TX" => Color.FromArgb(13, 132, 89),
                "RX" => Color.FromArgb(24, 96, 194),
                "ERR" => Color.FromArgb(190, 54, 54),
                _ => UiPalette.Muted
            };
            serialTrafficBox.AppendText($"{entry.Time:HH:mm:ss.fff}  {entry.Direction,-3}  {content}\n");
        }
        if (serialTrafficBox.TextLength > 150000)
        {
            serialTrafficBox.Select(0, serialTrafficBox.TextLength - 110000);
            serialTrafficBox.SelectedText = string.Empty;
        }
        if (batch.Count > 0)
        {
            serialTrafficBox.SelectionStart = serialTrafficBox.TextLength;
            serialTrafficBox.ScrollToCaret();
            if (!serialWaveformPaused && contentTabs.SelectedIndex == 4)
                RefreshSerialWaveform();
        }
        FlushSimulationEvents();
    }
}
