using System.IO.Ports;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class MainForm : Form
{
    private readonly ModbusRtuClient client = new();
    private readonly ComboBox portComboBox = new();
    private readonly NumericUpDown slaveAddressInput = new();
    private readonly NumericUpDown pollIntervalInput = new();
    private readonly NumericUpDown outputMaskInput = new();
    private readonly Button connectButton = new();
    private readonly Button refreshPortsButton = new();
    private readonly Button pollNowButton = new();
    private readonly Button writeOutputButton = new();
    private readonly Button clearLogButton = new();
    private readonly Label connectionLabel = new();
    private readonly Label lastPollLabel = new();
    private readonly DataGridView dataGrid = new();
    private readonly RichTextBox logBox = new();
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly Dictionary<string, int> rowIndexes = new();
    private readonly SemaphoreSlim pollGate = new(1, 1);
    private CancellationTokenSource? pollCancellation;
    private Task? pollTask;
    private int pollIntervalMs = 1000;

    public MainForm()
    {
        Text = "工业现场数据采集终端";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1020, 650);
        Size = new Size(1240, 780);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(242, 245, 248);

        BuildUi();
        client.LogMessage += OnClientLog;
        pollIntervalInput.ValueChanged += (_, _) => pollIntervalMs = (int)pollIntervalInput.Value;
        pollIntervalMs = (int)pollIntervalInput.Value;
        FormClosing += OnFormClosing;
        RefreshPorts();
        SetConnectedUi(false);
        SetStatus("未连接");
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildStatusBar(), 0, 1);
        root.Controls.Add(BuildContent(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(25, 53, 80),
            Padding = new Padding(18, 10, 18, 8)
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "工业现场数据采集终端",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            Location = new Point(18, 8)
        };
        header.Controls.Add(title);

        var subtitle = new Label
        {
            AutoSize = true,
            Text = "Modbus RTU · RS485 · 采集板卡从机监视",
            ForeColor = Color.FromArgb(190, 211, 228),
            Location = new Point(20, 38)
        };
        header.Controls.Add(subtitle);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 5, 0, 0)
        };

        AddLabel(bar, "串口");
        portComboBox.Width = 105;
        portComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        bar.Controls.Add(portComboBox);

        refreshPortsButton.Text = "刷新";
        refreshPortsButton.Width = 58;
        refreshPortsButton.Click += (_, _) => RefreshPorts();
        bar.Controls.Add(refreshPortsButton);

        AddLabel(bar, "波特率");
        var baudLabel = new Label
        {
            AutoSize = false,
            Width = 66,
            Height = 28,
            Text = "115200",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(45, 76, 105),
            Margin = new Padding(3, 0, 10, 0)
        };
        bar.Controls.Add(baudLabel);

        AddLabel(bar, "从机地址");
        ConfigureNumeric(slaveAddressInput, 1, 247, 1, 55);
        bar.Controls.Add(slaveAddressInput);

        AddLabel(bar, "轮询(ms)");
        ConfigureNumeric(pollIntervalInput, 100, 60000, 1000, 70);
        bar.Controls.Add(pollIntervalInput);

        connectButton.Text = "连接串口";
        connectButton.Width = 94;
        connectButton.BackColor = Color.FromArgb(36, 166, 114);
        connectButton.ForeColor = Color.White;
        connectButton.FlatStyle = FlatStyle.Flat;
        connectButton.FlatAppearance.BorderSize = 0;
        connectButton.Click += OnConnectClicked;
        bar.Controls.Add(connectButton);

        pollNowButton.Text = "立即采集";
        pollNowButton.Width = 84;
        pollNowButton.Click += OnPollNowClicked;
        bar.Controls.Add(pollNowButton);

        header.Controls.Add(bar);
        return header;
    }

    private Control BuildStatusBar()
    {
        statusStrip.Dock = DockStyle.Fill;
        statusStrip.SizingGrip = false;
        statusStrip.BackColor = Color.White;
        statusLabel.Text = "未连接";
        statusLabel.ForeColor = Color.FromArgb(96, 106, 118);
        statusStrip.Items.Add(statusLabel);
        return statusStrip;
    }

    private Control BuildContent()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 690,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(224, 230, 236),
            Padding = new Padding(12, 10, 12, 12)
        };

        split.Panel1.Controls.Add(BuildDataPanel());
        split.Panel2.Controls.Add(BuildLogPanel());
        return split;
    }

    private Control BuildDataPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var title = SectionTitle("实时采集数据");
        panel.Controls.Add(title);

        lastPollLabel.AutoSize = true;
        lastPollLabel.Text = "最近采集：--";
        lastPollLabel.ForeColor = Color.FromArgb(102, 112, 123);
        lastPollLabel.Location = new Point(18, 45);
        panel.Controls.Add(lastPollLabel);

        var gridPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 70, 14, 56)
        };
        ConfigureDataGrid();
        gridPanel.Controls.Add(dataGrid);
        panel.Controls.Add(gridPanel);

        var writePanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(14, 8, 14, 8)
        };
        var writeLabel = new Label
        {
            AutoSize = true,
            Text = "数字输出寄存器 0x0002：",
            Location = new Point(14, 13)
        };
        writePanel.Controls.Add(writeLabel);
        ConfigureNumeric(outputMaskInput, 0, 1023, 0, 70);
        outputMaskInput.Location = new Point(166, 8);
        writePanel.Controls.Add(outputMaskInput);
        writeOutputButton.Text = "写入输出";
        writeOutputButton.Width = 80;
        writeOutputButton.Location = new Point(244, 8);
        writeOutputButton.Click += OnWriteOutputClicked;
        writePanel.Controls.Add(writeOutputButton);
        panel.Controls.Add(writePanel);
        return panel;
    }

    private void ConfigureDataGrid()
    {
        dataGrid.Dock = DockStyle.Fill;
        dataGrid.ReadOnly = true;
        dataGrid.AllowUserToAddRows = false;
        dataGrid.AllowUserToDeleteRows = false;
        dataGrid.AllowUserToResizeRows = false;
        dataGrid.RowHeadersVisible = false;
        dataGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dataGrid.MultiSelect = false;
        dataGrid.BackgroundColor = Color.White;
        dataGrid.BorderStyle = BorderStyle.None;
        dataGrid.GridColor = Color.FromArgb(226, 231, 236);
        dataGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        dataGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(234, 240, 245),
            ForeColor = Color.FromArgb(38, 53, 68),
            Font = new Font(Font, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        dataGrid.EnableHeadersVisualStyles = false;
        dataGrid.Columns.Add("name", "数据项");
        dataGrid.Columns.Add("address", "寄存器");
        dataGrid.Columns.Add("value", "当前值");
        dataGrid.Columns.Add("description", "说明");
        dataGrid.Columns[0].Width = 175;
        dataGrid.Columns[1].Width = 90;
        dataGrid.Columns[2].Width = 160;
        dataGrid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        AddDataRow("设备状态", "0x0000", "--", "bit0 IMU，bit1 AD7606，bit2/3 最近读取结果", "status");
        AddDataRow("数字输入", "0x0001", "--", "8 路输入位图", "digitalInputs");
        AddDataRow("数字输出", "0x0002", "--", "10 路输出位图，可写", "digitalOutputs");
        for (var index = 0; index < 8; index++)
        {
            AddDataRow($"AD7606 CH{index + 1}", $"0x{0x10 + index:X4}", "--", "有符号原始采样值", $"ad{index}");
        }

        var imuNames = new[] { "加速度 X", "加速度 Y", "加速度 Z", "陀螺仪 X", "陀螺仪 Y", "陀螺仪 Z" };
        for (var index = 0; index < imuNames.Length; index++)
        {
            AddDataRow(imuNames[index], $"0x{0x20 + index:X4}", "--", "IMU 有符号原始值", $"imu{index}");
        }

        AddDataRow("Roll", "0x0030", "--", "单位 0.01°", "roll");
        AddDataRow("Pitch", "0x0031", "--", "单位 0.01°", "pitch");
        AddDataRow("Yaw", "0x0032", "--", "单位 0.01°", "yaw");
    }

    private Control BuildLogPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        panel.Controls.Add(SectionTitle("通信日志"));

        logBox.Dock = DockStyle.Fill;
        logBox.BorderStyle = BorderStyle.None;
        logBox.BackColor = Color.FromArgb(22, 29, 37);
        logBox.ForeColor = Color.FromArgb(218, 228, 237);
        logBox.Font = new Font("Consolas", 9F);
        logBox.ReadOnly = true;
        logBox.WordWrap = false;
        logBox.ScrollBars = RichTextBoxScrollBars.Both;
        logBox.DetectUrls = false;
        logBox.Margin = new Padding(0);
        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 52, 14, 52) };
        logHost.Controls.Add(logBox);
        panel.Controls.Add(logHost);

        var logFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(14, 7, 14, 7)
        };
        clearLogButton.Text = "清空日志";
        clearLogButton.Width = 82;
        clearLogButton.Click += (_, _) => logBox.Clear();
        logFooter.Controls.Add(clearLogButton);
        panel.Controls.Add(logFooter);
        return panel;
    }

    private static Label SectionTitle(string text)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 42,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 53, 70),
            BackColor = Color.FromArgb(248, 250, 252)
        };
    }

    private static void AddLabel(FlowLayoutPanel panel, string text)
    {
        panel.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 58,
            Height = 28,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(220, 232, 242),
            Margin = new Padding(8, 0, 2, 0)
        });
    }

    private static void ConfigureNumeric(NumericUpDown input, decimal minimum, decimal maximum, decimal value, int width)
    {
        input.Minimum = minimum;
        input.Maximum = maximum;
        input.Value = value;
        input.Width = width;
        input.Height = 28;
        input.TextAlign = HorizontalAlignment.Center;
        input.Margin = new Padding(3, 0, 8, 0);
    }

    private void AddDataRow(string name, string address, string value, string description, string key)
    {
        var row = dataGrid.Rows.Add(name, address, value, description);
        rowIndexes[key] = row;
    }

    private void RefreshPorts()
    {
        var selected = portComboBox.Text;
        portComboBox.Items.Clear();
        portComboBox.Items.AddRange(SerialPort.GetPortNames().OrderBy(name => name).ToArray());
        if (portComboBox.Items.Contains(selected))
        {
            portComboBox.SelectedItem = selected;
        }
        else if (portComboBox.Items.Count > 0)
        {
            portComboBox.SelectedIndex = 0;
        }

        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  检测到串口：{(portComboBox.Items.Count == 0 ? "无" : string.Join(", ", portComboBox.Items.Cast<string>()))}");
    }

    private void OnConnectClicked(object? sender, EventArgs e)
    {
        if (client.IsOpen)
        {
            Disconnect();
            return;
        }

        try
        {
            client.Open(portComboBox.Text, 115200, (byte)slaveAddressInput.Value);
            pollCancellation = new CancellationTokenSource();
            var token = pollCancellation.Token;
            pollTask = Task.Run(() => PollLoopAsync(token), token);
            SetConnectedUi(true);
            SetStatus("已连接，正在轮询");
        }
        catch (Exception ex)
        {
            SetStatus("连接失败");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  {ex.Message}");
            client.Close();
        }
    }

    private void Disconnect()
    {
        pollCancellation?.Cancel();
        client.Close();
        pollCancellation?.Dispose();
        pollCancellation = null;
        pollTask = null;
        SetConnectedUi(false);
        SetStatus("未连接");
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  串口已关闭");
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await PollOnceAsync(token);
            try
            {
                await Task.Delay(pollIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken token)
    {
        if (!client.IsOpen || token.IsCancellationRequested)
        {
            return;
        }

        await pollGate.WaitAsync(token);
        try
        {
            var status = client.ReadHoldingRegisters(0x0000, 3);
            var ad7606 = client.ReadHoldingRegisters(0x0010, 8);
            var imu = client.ReadHoldingRegisters(0x0020, 6);
            var angles = client.ReadHoldingRegisters(0x0030, 3);

            var snapshot = new AcquisitionSnapshot
            {
                Timestamp = DateTime.Now,
                Status = status[0],
                DigitalInputs = status[1],
                DigitalOutputs = status[2],
                Ad7606Raw = ad7606.Select(value => unchecked((short)value)).ToArray(),
                ImuRaw = imu.Select(value => unchecked((short)value)).ToArray(),
                RollCentiDegrees = unchecked((short)angles[0]),
                PitchCentiDegrees = unchecked((short)angles[1]),
                YawCentiDegrees = unchecked((short)angles[2])
            };

            Ui(() => UpdateSnapshot(snapshot));
            SetStatus($"在线 · 最后响应 {snapshot.Timestamp:HH:mm:ss}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  {ex.Message}");
            SetStatus("通信异常，请查看日志");
        }
        finally
        {
            pollGate.Release();
        }
    }

    private void OnPollNowClicked(object? sender, EventArgs e)
    {
        if (!client.IsOpen)
        {
            SetStatus("请先连接串口");
            return;
        }

        _ = Task.Run(() => PollOnceAsync(CancellationToken.None));
    }

    private void OnWriteOutputClicked(object? sender, EventArgs e)
    {
        if (!client.IsOpen)
        {
            SetStatus("请先连接串口");
            return;
        }

        var value = (ushort)outputMaskInput.Value;
        _ = Task.Run(async () =>
        {
            await pollGate.WaitAsync();
            try
            {
                client.WriteSingleRegister(0x0002, value);
                OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  输出寄存器写入成功：0x{value:X4}");
            }
            catch (Exception ex)
            {
                OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  写输出失败：{ex.Message}");
            }
            finally
            {
                pollGate.Release();
            }
        });
    }

    private void UpdateSnapshot(AcquisitionSnapshot snapshot)
    {
        SetValue("status", $"0x{snapshot.Status:X4}");
        SetValue("digitalInputs", $"0x{snapshot.DigitalInputs:X4}");
        SetValue("digitalOutputs", $"0x{snapshot.DigitalOutputs:X4}");
        outputMaskInput.Value = Math.Min(outputMaskInput.Maximum, snapshot.DigitalOutputs);
        SetValue("status", $"0x{snapshot.Status:X4} ({DescribeStatus(snapshot.Status)})");

        for (var index = 0; index < snapshot.Ad7606Raw.Length; index++)
        {
            SetValue($"ad{index}", snapshot.Ad7606Raw[index].ToString());
        }

        for (var index = 0; index < snapshot.ImuRaw.Length; index++)
        {
            SetValue($"imu{index}", snapshot.ImuRaw[index].ToString());
        }

        SetValue("roll", FormatAngle(snapshot.RollCentiDegrees));
        SetValue("pitch", FormatAngle(snapshot.PitchCentiDegrees));
        SetValue("yaw", FormatAngle(snapshot.YawCentiDegrees));
        lastPollLabel.Text = $"最近采集：{snapshot.Timestamp:yyyy-MM-dd HH:mm:ss.fff}";
    }

    private static string DescribeStatus(ushort status)
    {
        var flags = new List<string>();
        if ((status & 0x0001) != 0) flags.Add("IMU");
        if ((status & 0x0002) != 0) flags.Add("AD7606");
        if ((status & 0x0004) != 0) flags.Add("IMU读取OK");
        if ((status & 0x0008) != 0) flags.Add("AD读取OK");
        return flags.Count == 0 ? "无就绪标志" : string.Join(", ", flags);
    }

    private static string FormatAngle(short centiDegrees)
    {
        return $"{centiDegrees / 100.0:0.00}°";
    }

    private void SetValue(string key, string value)
    {
        if (rowIndexes.TryGetValue(key, out var row))
        {
            dataGrid.Rows[row].Cells[2].Value = value;
        }
    }

    private void SetConnectedUi(bool connected)
    {
        portComboBox.Enabled = !connected;
        slaveAddressInput.Enabled = !connected;
        refreshPortsButton.Enabled = !connected;
        connectButton.Text = connected ? "断开串口" : "连接串口";
        connectButton.BackColor = connected ? Color.FromArgb(196, 78, 70) : Color.FromArgb(36, 166, 114);
        pollNowButton.Enabled = connected;
        writeOutputButton.Enabled = connected;
    }

    private void SetStatus(string text)
    {
        Ui(() =>
        {
            statusLabel.Text = text;
            connectionLabel.Text = text;
        });
    }

    private void OnClientLog(string message)
    {
        Ui(() =>
        {
            if (logBox.IsDisposed)
            {
                return;
            }

            logBox.AppendText(message + Environment.NewLine);
            if (logBox.Lines.Length > 1200)
            {
                logBox.Select(0, logBox.GetFirstCharIndexFromLine(200));
                logBox.SelectedText = string.Empty;
            }

            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        });
    }

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        pollCancellation?.Cancel();
        client.Dispose();
        pollGate.Dispose();
    }
}
