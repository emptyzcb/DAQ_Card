using System.Globalization;
using System.IO.Ports;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private readonly ModbusRtuSlaveSimulator modbusSimulator = new();
    private readonly SerialConnection simulationConnection = new();
    private readonly ComboBox simulationPortInput = new(), simulationBaudInput = new(), simulationDataBitsInput = new(), simulationParityInput = new(), simulationStopBitsInput = new();
    private readonly Button simulationRefreshButton = new();
    private bool simulationOpening;
    private readonly RawFrameResponder rawResponder = new();
    private readonly object simulationLock = new();
    private readonly object simulationEventLock = new();
    private readonly Queue<SimulatedReply> simulationEvents = new();
    private readonly TabControl simulationTabs = new();
    private readonly DataGridView virtualSlaveGrid = new();
    private readonly DataGridView rawRuleGrid = new();
    private readonly Button simulationStartButton = new();
    private readonly Button simulationAddButton = new();
    private readonly Button simulationRemoveButton = new();
    private readonly Label simulationStatusLabel = new();
    private readonly RichTextBox simulationLogBox = new();
    private volatile bool simulationRunning;
    private int simulationMode;
    private int simulationResponseCount;

    private Control BuildTestPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = UiPalette.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        root.Controls.Add(SectionTitle("通信测试与虚拟设备"), 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(14, 9, 12, 5),
            BackColor = UiPalette.Surface
        };
        simulationAddButton.Text = "添加";
        simulationAddButton.Size = new Size(70, 30);
        UiPalette.StyleButton(simulationAddButton);
        simulationAddButton.Click += (_, _) =>
        {
            if (simulationTabs.SelectedIndex == 0) AddVirtualSlave();
            else AddRawRule();
        };
        toolbar.Controls.Add(simulationAddButton);
        simulationRemoveButton.Text = "删除选中";
        simulationRemoveButton.Size = new Size(88, 30);
        UiPalette.StyleButton(simulationRemoveButton);
        simulationRemoveButton.Click += (_, _) =>
        {
            var grid = simulationTabs.SelectedIndex == 0 ? virtualSlaveGrid : rawRuleGrid;
            foreach (DataGridViewRow row in grid.SelectedRows.Cast<DataGridViewRow>()
                         .OrderByDescending(row => row.Index)) grid.Rows.RemoveAt(row.Index);
        };
        toolbar.Controls.Add(simulationRemoveButton);
        simulationStartButton.Text = "启动模拟";
        simulationStartButton.Size = new Size(90, 30);
        UiPalette.StyleButton(simulationStartButton, primary: true);
        simulationStartButton.Click += async (_, _) => await ToggleSimulationAsync();
        toolbar.Controls.Add(simulationStartButton);
        simulationStatusLabel.AutoSize = true;
        simulationStatusLabel.ForeColor = UiPalette.Muted;
        simulationStatusLabel.Margin = new Padding(16, 7, 0, 0);
        simulationStatusLabel.Text = "串口未连接";
        toolbar.Controls.Add(simulationStatusLabel);
        root.Controls.Add(toolbar, 0, 1);

        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true,
            Padding = new Padding(14, 7, 12, 2), BackColor = UiPalette.Surface
        };
        static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(2, 8, 4, 0), ForeColor = UiPalette.Muted };
        simulationPortInput.DropDownStyle = ComboBoxStyle.DropDownList;
        simulationPortInput.Width = 90;
        simulationPortInput.Margin = new Padding(0, 4, 6, 0);
        simulationPortInput.DropDown += (_, _) => RefreshSimulationPorts();
        simulationRefreshButton.Text = "刷新"; simulationRefreshButton.Size = new Size(58, 29);
        simulationRefreshButton.Margin = new Padding(0, 2, 12, 0);
        UiPalette.StyleButton(simulationRefreshButton);
        simulationRefreshButton.Click += (_, _) => RefreshSimulationPorts();
        simulationBaudInput.DropDownStyle = ComboBoxStyle.DropDown;
        simulationBaudInput.Width = 95;
        simulationBaudInput.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600" });
        simulationBaudInput.Text = "115200";
        SetupSerialChoice(simulationDataBitsInput, 53, "8", "5", "6", "7", "8");
        SetupSerialChoice(simulationParityInput, 57, "无", "无", "偶", "奇");
        SetupSerialChoice(simulationStopBitsInput, 53, "1", "1", "1.5", "2");
        foreach (var combo in new[] { simulationBaudInput, simulationDataBitsInput, simulationParityInput, simulationStopBitsInput }) combo.Margin = new Padding(0, 4, 9, 0);
        settings.Controls.AddRange(new Control[] { FieldLabel("串口"), simulationPortInput, simulationRefreshButton,
            FieldLabel("波特率"), simulationBaudInput, FieldLabel("数据位"), simulationDataBitsInput,
            FieldLabel("校验"), simulationParityInput, FieldLabel("停止位"), simulationStopBitsInput });
        root.Controls.Add(settings, 0, 2);
        RefreshSimulationPorts();
        simulationConnection.Received += OnSimulationReceived;
        simulationConnection.Error += ex => Ui(() => { if (!IsDisposed) { StopSimulation(); simulationStatusLabel.Text = "串口异常：" + ex.Message; } });

        simulationTabs.Dock = DockStyle.Fill;
        simulationTabs.Font = Font;
        simulationTabs.SelectedIndexChanged += (_, _) =>
        {
            simulationMode = simulationTabs.SelectedIndex;
            UpdateTestSerialStatus();
        };
        var modbusPage = new TabPage("Modbus RTU 虚拟从机")
        {
            BackColor = UiPalette.Surface,
            Padding = new Padding(11, 10, 11, 11)
        };
        var modbusBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = UiPalette.Surface
        };
        modbusBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        modbusBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        modbusBody.Controls.Add(TestHint("一个串口可模拟多个从站；寄存器为 HEX。每台可选择故障模式；修改后重新启动模拟生效。"), 0, 0);
        ConfigureVirtualSlaveGrid();
        modbusBody.Controls.Add(virtualSlaveGrid, 0, 1);
        modbusPage.Controls.Add(modbusBody);

        var rawPage = new TabPage("自定义 HEX 帧")
        {
            BackColor = UiPalette.Surface,
            Padding = new Padding(11, 10, 11, 11)
        };
        var rawBody = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = UiPalette.Surface
        };
        rawBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        rawBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rawBody.Controls.Add(TestHint("精确匹配收到的请求字节后发送响应。适合验证其他串口协议的固定帧，不会解析协议字段。"), 0, 0);
        ConfigureRawRuleGrid();
        rawBody.Controls.Add(rawRuleGrid, 0, 1);
        rawPage.Controls.Add(rawBody);
        simulationTabs.TabPages.Add(modbusPage);
        simulationTabs.TabPages.Add(rawPage);
        root.Controls.Add(simulationTabs, 0, 3);

        var logHost = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(14, 5, 14, 10),
            BackColor = UiPalette.Surface
        };
        logHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        logHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logHost.Controls.Add(TestHint("模拟响应记录"), 0, 0);
        simulationLogBox.Dock = DockStyle.Fill;
        simulationLogBox.ReadOnly = true;
        simulationLogBox.BorderStyle = BorderStyle.None;
        simulationLogBox.BackColor = UiPalette.Surface;
        simulationLogBox.ForeColor = UiPalette.LogText;
        simulationLogBox.Font = new Font("Cascadia Mono", 9F);
        logHost.Controls.Add(simulationLogBox, 0, 1);
        root.Controls.Add(logHost, 0, 4);
        AddVirtualSlave();
        AddRawRule();
        return root;
    }

    private static Label TestHint(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = UiPalette.Muted
    };

    private void ConfigureVirtualSlaveGrid()
    {
        ConfigureTestGrid(virtualSlaveGrid);
        virtualSlaveGrid.Columns.Add(new DataGridViewCheckBoxColumn
            { Name = "enabled", HeaderText = "启用", Width = 54 });
        virtualSlaveGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "name", HeaderText = "虚拟设备", Width = 145 });
        virtualSlaveGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "address", HeaderText = "从站地址", Width = 90 });
        virtualSlaveGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "start", HeaderText = "起始寄存器", Width = 105 });
        virtualSlaveGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "holding", HeaderText = "03 保持寄存器 HEX", MinimumWidth = 200,
              AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        virtualSlaveGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "input", HeaderText = "04 输入寄存器 HEX", MinimumWidth = 200,
              AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        var faultColumn = new DataGridViewComboBoxColumn
            { Name = "fault", HeaderText = "故障模式", Width = 115, FlatStyle = FlatStyle.Flat };
        faultColumn.Items.AddRange("正常", "超时不应答", "CRC 错误", "异常 02", "异常 04");
        virtualSlaveGrid.Columns.Add(faultColumn);
        virtualSlaveGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "requests", HeaderText = "请求次数", Width = 85, ReadOnly = true });
    }

    private void ConfigureRawRuleGrid()
    {
        ConfigureTestGrid(rawRuleGrid);
        rawRuleGrid.Columns.Add(new DataGridViewCheckBoxColumn
            { Name = "enabled", HeaderText = "启用", Width = 54 });
        rawRuleGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "name", HeaderText = "规则名称", Width = 150 });
        rawRuleGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "request", HeaderText = "请求 HEX", MinimumWidth = 230,
              AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        rawRuleGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "response", HeaderText = "响应 HEX", MinimumWidth = 230,
              AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        rawRuleGrid.Columns.Add(new DataGridViewTextBoxColumn
            { Name = "matches", HeaderText = "响应次数", Width = 85, ReadOnly = true });
    }

    private void ConfigureTestGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = true;
        grid.BackgroundColor = UiPalette.Surface;
        grid.BorderStyle = BorderStyle.None;
        ConfigureOperationalGridStyle(grid);
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiPalette.Canvas,
            ForeColor = UiPalette.Ink,
            Font = new Font(Font, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0)
        };
        grid.DataError += (_, _) => { };
    }

    private void AddVirtualSlave()
    {
        var occupied = virtualSlaveGrid.Rows.Cast<DataGridViewRow>()
            .Select(row => int.TryParse(row.Cells["address"].Value?.ToString(), out var value) ? value : 0)
            .ToHashSet();
        var address = Enumerable.Range(1, 247).FirstOrDefault(value => !occupied.Contains(value));
        if (address == 0) return;
        virtualSlaveGrid.Rows.Add(true, $"虚拟从站 {address}", address, "0x0000",
            "0001 0002 0003 0004", "1001 1002 1003 1004", "正常", 0);
    }

    private void AddRawRule() => rawRuleGrid.Rows.Add(true, "自定义响应", "AA 55 01", "AA 55 01 00", 0);

    private IReadOnlyList<VirtualRs485Slave> ReadVirtualSlaves()
    {
        virtualSlaveGrid.EndEdit();
        var definitions = new List<VirtualRs485Slave>();
        foreach (DataGridViewRow row in virtualSlaveGrid.Rows)
        {
            if (row.Cells["enabled"].Value is not true) continue;
            var name = row.Cells["name"].Value?.ToString() ?? $"第 {row.Index + 1} 行";
            if (!byte.TryParse(row.Cells["address"].Value?.ToString(), out var address) || address is < 1 or > 247)
                throw new FormatException($"{name}：从站地址应为 1–247。");
            var startText = row.Cells["start"].Value?.ToString()?.Trim() ?? "";
            var hexStart = startText.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!ushort.TryParse(hexStart ? startText[2..] : startText,
                    hexStart ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var start))
                throw new FormatException($"{name}：起始寄存器无效。");
            var holding = ParseRegisterValues(row.Cells["holding"].Value?.ToString(), name, "03");
            var input = ParseRegisterValues(row.Cells["input"].Value?.ToString(), name, "04");
            var fault = row.Cells["fault"].Value?.ToString() switch
            {
                "正常" => VirtualSlaveFault.None,
                "超时不应答" => VirtualSlaveFault.Timeout,
                "CRC 错误" => VirtualSlaveFault.BadCrc,
                "异常 02" => VirtualSlaveFault.IllegalAddress,
                "异常 04" => VirtualSlaveFault.DeviceFailure,
                _ => throw new FormatException($"{name}：请选择有效的故障模式。")
            };
            if (start + Math.Max(holding.Length, input.Length) > 65536)
                throw new FormatException($"{name}：寄存器范围超出 0xFFFF。 ");
            definitions.Add(new VirtualRs485Slave(address, start, holding, input, fault));
        }
        if (definitions.Count == 0) throw new FormatException("请至少启用一个虚拟从站。");
        return definitions;
    }

    private static ushort[] ParseRegisterValues(string? text, string name, string function)
    {
        var tokens = (text ?? "").Split(new[] { ' ', '\t', '\r', '\n', ',', ';' },
            StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 1 or > 16)
            throw new FormatException($"{name}：{function} 寄存器需要 1–16 个 HEX 值。");
        var result = new ushort[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? tokens[index][2..] : tokens[index];
            if (!ushort.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[index]))
                throw new FormatException($"{name}：{function} 寄存器第 {index + 1} 项不是 HEX 字值。");
        }
        return result;
    }

    private IReadOnlyList<RawResponseRule> ReadRawRules()
    {
        rawRuleGrid.EndEdit();
        var rules = new List<RawResponseRule>();
        foreach (DataGridViewRow row in rawRuleGrid.Rows)
        {
            if (row.Cells["enabled"].Value is not true) continue;
            var name = row.Cells["name"].Value?.ToString() ?? $"第 {row.Index + 1} 行";
            var request = ParseHexBytes(row.Cells["request"].Value?.ToString() ?? "");
            var response = ParseHexBytes(row.Cells["response"].Value?.ToString() ?? "");
            if (request.Length == 0 || response.Length == 0)
                throw new FormatException($"{name}：请求和响应都不能留空。");
            rules.Add(new RawResponseRule(request, response));
        }
        if (rules.Count == 0) throw new FormatException("请至少启用一条自定义响应规则。");
        return rules;
    }

    private void RefreshSimulationPorts()
    {
        var selected = simulationPortInput.Text;
        var ports = SerialPort.GetPortNames().OrderBy(name =>
            int.TryParse(name.AsSpan(3), out var number) ? number : int.MaxValue).ToArray();
        simulationPortInput.Items.Clear();
        simulationPortInput.Items.AddRange(ports.Cast<object>().ToArray());
        simulationPortInput.SelectedItem = ports.Contains(selected) ? selected : ports.FirstOrDefault();
    }

    private async Task ToggleSimulationAsync()
    {
        if (simulationOpening) return;
        if (simulationRunning) { StopSimulation(); return; }
        if (simulationPortInput.SelectedItem is not string portName)
        { simulationStatusLabel.Text = "请选择串口；连接适配器后可点击刷新。"; return; }
        if (!int.TryParse(simulationBaudInput.Text, out var baud) || baud is < 300 or > 4000000)
        { simulationStatusLabel.Text = "波特率请输入 300–4000000。"; return; }
        if (serialConnection.IsOpen && string.Equals(serialConnection.PortName, portName, StringComparison.OrdinalIgnoreCase))
        { simulationStatusLabel.Text = $"{portName} 已被串口工具使用，请先关闭该端口或选择另一端口。"; return; }
        try
        {
            if (simulationTabs.SelectedIndex == 0) modbusSimulator.Configure(ReadVirtualSlaves());
            else rawResponder.Configure(ReadRawRules());
            var parity = simulationParityInput.Text switch { "偶" => Parity.Even, "奇" => Parity.Odd, _ => Parity.None };
            var stop = simulationStopBitsInput.Text switch { "1.5" => StopBits.OnePointFive, "2" => StopBits.Two, _ => StopBits.One };
            var options = new SerialConnectionOptions(portName, baud, int.Parse(simulationDataBitsInput.Text), parity, stop);
            simulationMode = simulationTabs.SelectedIndex;
            simulationOpening = true; UpdateTestSerialStatus();
            await Task.Run(() => simulationConnection.Open(options));
            if (IsDisposed || Disposing) { simulationConnection.Close(); return; }
            simulationResponseCount = 0;
            simulationRunning = true;
            simulationLogBox.AppendText($"{DateTime.Now:HH:mm:ss.fff}  已启动 · {portName} · {baud} bps · " +
                $"{(simulationMode == 0 ? "Modbus RTU" : "自定义 HEX 帧")}\n");
        }
        catch (Exception ex)
        {
            simulationConnection.Close();
            if (!IsDisposed && !Disposing) simulationStatusLabel.Text = $"无法启动：{ex.Message}（请检查串口参数及端口占用）";
            return;
        }
        finally
        {
            simulationOpening = false;
            if (!IsDisposed && !Disposing) UpdateTestSerialStatus();
        }
    }

    private void StopSimulation()
    {
        lock (simulationLock) simulationRunning = false;
        simulationConnection.Close();
        UpdateTestSerialStatus();
    }

    private void UpdateTestSerialStatus()
    {
        if (simulationStatusLabel.IsDisposed) return;
        var canEdit = !simulationRunning && !simulationOpening;
        simulationTabs.Enabled = canEdit;
        simulationAddButton.Enabled = canEdit;
        simulationRemoveButton.Enabled = canEdit;
        simulationStartButton.Enabled = !simulationOpening;
        simulationStartButton.Text = simulationOpening ? "正在打开" : simulationRunning ? "停止模拟" : "启动模拟";
        simulationStartButton.BackColor = simulationRunning ? UiPalette.Red : UiPalette.Blue;
        foreach (var control in new Control[] { simulationPortInput, simulationRefreshButton, simulationBaudInput, simulationDataBitsInput, simulationParityInput, simulationStopBitsInput }) control.Enabled = canEdit;
        if (simulationRunning) simulationStatusLabel.Text = $"正在通过 {simulationConnection.PortName} 模拟 · 已处理 {simulationResponseCount} 次请求";
        else if (simulationOpening) simulationStatusLabel.Text = "正在打开串口…";
        else if (!simulationStatusLabel.Text.StartsWith("无法启动：") && !simulationStatusLabel.Text.StartsWith("串口异常："))
            simulationStatusLabel.Text = "选择串口与参数后，点击启动模拟";
    }

    private void OnSimulationReceived(byte[] bytes)
    {
        if (!simulationRunning) return;
        lock (simulationLock)
        {
            if (!simulationRunning) return;
            try
            {
                var replies = simulationMode == 0 ? modbusSimulator.Process(bytes) : rawResponder.Process(bytes);
                foreach (var reply in replies)
                {
                    if (reply.Response.Length != 0) simulationConnection.Send(reply.Response);
                    QueueSimulationEvent(reply);
                }
            }
            catch (Exception ex) { Ui(() => { StopSimulation(); simulationStatusLabel.Text = "模拟响应失败：" + ex.Message; }); }
        }
    }

    private void QueueSimulationEvent(SimulatedReply reply)
    {
        lock (simulationEventLock)
        {
            if (simulationEvents.Count >= 1000) simulationEvents.Dequeue();
            simulationEvents.Enqueue(reply);
        }
    }

    private void FlushSimulationEvents()
    {
        var batch = new List<SimulatedReply>();
        lock (simulationEventLock)
        {
            while (simulationEvents.Count > 0 && batch.Count < 100) batch.Add(simulationEvents.Dequeue());
        }
        foreach (var reply in batch)
        {
            simulationResponseCount++;
            var label = simulationMode == 0 ? $"地址 {reply.Address} FC{reply.Function:X2}" : "自定义帧";
            simulationLogBox.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {label}  " +
                $"{BitConverter.ToString(reply.Request).Replace('-', ' ')} → " +
                $"{(reply.Response.Length == 0 ? "超时：不应答" : BitConverter.ToString(reply.Response).Replace('-', ' '))}\n");
            var grid = simulationMode == 0 ? virtualSlaveGrid : rawRuleGrid;
            var countColumn = simulationMode == 0 ? "requests" : "matches";
            var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(item =>
                simulationMode == 0
                    ? item.Cells["address"].Value?.ToString() == reply.Address.ToString()
                    : string.Equals(item.Cells["request"].Value?.ToString()?.Replace(" ", ""),
                        Convert.ToHexString(reply.Request), StringComparison.OrdinalIgnoreCase));
            if (row is not null)
                row.Cells[countColumn].Value = (int.TryParse(row.Cells[countColumn].Value?.ToString(), out var count)
                    ? count : 0) + 1;
        }
        if (simulationLogBox.TextLength > 90000)
        {
            simulationLogBox.Select(0, simulationLogBox.TextLength - 65000);
            simulationLogBox.SelectedText = string.Empty;
        }
        if (batch.Count > 0)
        {
            simulationLogBox.SelectionStart = simulationLogBox.TextLength;
            simulationLogBox.ScrollToCaret();
            UpdateTestSerialStatus();
        }
    }
}
