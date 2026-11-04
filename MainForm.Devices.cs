namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private readonly DeviceCatalog deviceCatalog = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IndustrialFieldDataAcquisitionTerminal", "devices.json"));
    private DeviceCatalogSnapshot? deviceSnapshot;
    private readonly DataGridView deviceGrid = new();
    private readonly Label deviceInfo = new();
    private readonly Button deviceConnectButton = new();
    private Guid? selectedDeviceId;
    private bool deviceWorkspaceOpen;
    private string? trendDeviceEndpoint;

    private Control BuildDevicePage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = UiPalette.Surface };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(SectionTitle("我的设备 · DAQ-800 多源采集控制器"), 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 5, 0, 0) };
        void AddButton(string text, Action action, bool primary = false)
        {
            var button = new Button { Text = text, Width = 94, Height = 30 };
            UiPalette.StyleButton(button, primary);
            button.Click += (_, _) => action();
            toolbar.Controls.Add(button);
        }
        AddButton("创建设备", CreateDevice, primary: true);
        AddButton("编辑设备", () => { if (SelectedDevice() is { } profile) EditDevice(profile); });
        AddButton("删除设备", DeleteDevice);
        AddButton("重新加载", LoadDeviceCatalog);
        deviceConnectButton.Text = "进入设备";
        deviceConnectButton.Size = new Size(94, 30);
        UiPalette.StyleButton(deviceConnectButton, primary: true);
        deviceConnectButton.Click += (_, _) => OpenSelectedDevice();
        toolbar.Controls.Add(deviceConnectButton);
        AddButton("返回当前设备", () =>
        {
            if (deviceSnapshot?.Devices.FirstOrDefault(item => item.Id == selectedDeviceId) is { } profile)
                OpenDeviceWorkspace(profile);
            else deviceInfo.Text = "请先选择一个设备进入工作区。";
        });
        root.Controls.Add(toolbar, 0, 1);
        ConfigureTestGrid(deviceGrid);
        deviceGrid.MultiSelect = false;
        deviceGrid.ReadOnly = true;
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "设备名称", Width = 170 });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "type", HeaderText = "产品型号", Width = 115 });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "endpoint", HeaderText = "以太网地址", Width = 190 });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "unit", HeaderText = "Unit ID", Width = 75 });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "interval", HeaderText = "轮询 ms", Width = 85 });
        deviceGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "notes", HeaderText = "备注", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        deviceGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) OpenSelectedDevice(); };
        root.Controls.Add(deviceGrid, 0, 2);
        deviceInfo.Dock = DockStyle.Fill;
        deviceInfo.Padding = new Padding(12, 4, 12, 0);
        deviceInfo.ForeColor = UiPalette.Muted;
        deviceInfo.Text = "创建设备：先选择产品，再填写名称和连接参数。双击设备进入工作区，串口与通信测试始终可用。";
        root.Controls.Add(deviceInfo, 0, 3);
        return root;
    }

    private void InitializeDeviceCatalog()
    {
        LoadDeviceCatalog();
        hostInput.TextChanged += (_, _) => UpdateDeviceConnectionState();
        ethernetPortInput.ValueChanged += (_, _) => UpdateDeviceConnectionState();
        slaveAddressInput.ValueChanged += (_, _) => UpdateDeviceConnectionState();
    }

    private void LoadDeviceCatalog()
    {
        if (workbenchSensors is not null)
        {
            RefreshDeviceGrid();
            UpdateDeviceConnectionState();
            deviceInfo.Text = "当前使用工程中的设备档案；保存工程可保留修改。";
            return;
        }
        try
        {
            deviceSnapshot = deviceCatalog.Load();
            // Preserve the legacy single-board sensor list when moving to device-scoped storage.
            if (deviceSnapshot.Devices.Count == 1 && File.Exists(LegacyRs485ConfigurationPath))
            {
                var path = DeviceSensorConfigurationPath(deviceSnapshot.Devices[0].Id);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.Copy(LegacyRs485ConfigurationPath, path, overwrite: false);
                }
            }
            RefreshDeviceGrid();
        }
        catch (Exception ex)
        {
            deviceSnapshot = null;
            deviceGrid.Rows.Clear();
            deviceInfo.Text = $"设备档案无法读取：{ex.Message}。原文件已保留，请修复后重新加载。";
        }
        UpdateDeviceConnectionState();
    }

    private void RefreshDeviceGrid()
    {
        var selected = SelectedDevice()?.Id;
        deviceGrid.Rows.Clear();
        foreach (var profile in deviceSnapshot?.Devices ?? Array.Empty<DeviceProfile>())
        {
            var index = deviceGrid.Rows.Add(profile.Name, profile.Type, $"{profile.Host}:{profile.Port}", profile.UnitId, profile.PollIntervalMs, profile.Notes);
            deviceGrid.Rows[index].Tag = profile.Id;
            if (profile.Id == selected) deviceGrid.CurrentCell = deviceGrid.Rows[index].Cells[0];
        }
    }

    private DeviceProfile? SelectedDevice()
    {
        if (deviceGrid.CurrentRow?.Tag is not Guid id) return null;
        return deviceSnapshot?.Devices.FirstOrDefault(item => item.Id == id);
    }

    private void EditDevice(DeviceProfile? existing)
    {
        if (deviceSnapshot is null) { LoadDeviceCatalog(); if (deviceSnapshot is null) return; }
        using var dialog = new DeviceProfileDialog(existing);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Profile is null) return;
        if ((client.IsOpen || isConnecting) && existing?.Id == selectedDeviceId)
        {
            deviceInfo.Text = "当前设备正在连接，请先断开后再修改该设备档案。";
            return;
        }
        var items = deviceSnapshot.Devices.Where(p => p.Id != dialog.Profile.Id).Append(dialog.Profile).ToArray();
        SaveDevices(items);
    }

    private void CreateDevice()
    {
        if (deviceSnapshot is null) { LoadDeviceCatalog(); if (deviceSnapshot is null) return; }
        using var picker = new DeviceModelPickerDialog();
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedModel != DeviceCatalog.ProductModel) return;
        // The catalog currently supports one product; selection precedes instance settings.
        EditDevice(null);
    }

    private void DeleteDevice()
    {
        var selected = SelectedDevice();
        if (selected is null || deviceSnapshot is null) return;
        if ((client.IsOpen || isConnecting) && selected.Id == selectedDeviceId)
        {
            deviceInfo.Text = "当前设备正在连接，请先断开后再删除档案。";
            return;
        }
        if (MessageBox.Show(this, $"删除设备档案“{selected.Name}”？这不会修改板卡。", "删除设备",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        SaveDevices(deviceSnapshot.Devices.Where(p => p.Id != selected.Id).ToArray());
    }

    private void SaveDevices(IReadOnlyCollection<DeviceProfile> profiles)
    {
        try
        {
            var previousActive = deviceSnapshot?.Devices.FirstOrDefault(p => p.Id == selectedDeviceId);
            if (workbenchSensors is null) deviceSnapshot = deviceCatalog.Save(profiles, deviceSnapshot!.Revision);
            else
            {
                DeviceCatalog.Validate(profiles);
                deviceSnapshot = new(profiles.ToArray(), "workbench");
                foreach (var id in workbenchSensors.Keys.Except(profiles.Select(p => p.Id)).ToArray()) { workbenchSensors.Remove(id); workbenchEngineering?.Remove(id); }
                MarkWorkbenchDirty();
            }
            var updatedActive = profiles.FirstOrDefault(p => p.Id == selectedDeviceId);
            if (updatedActive is not null && updatedActive != previousActive && !client.IsOpen && !isConnecting)
            {
                hostInput.Text = updatedActive.Host;
                ethernetPortInput.Value = updatedActive.Port;
                slaveAddressInput.Value = updatedActive.UnitId;
                pollIntervalInput.Value = updatedActive.PollIntervalMs;
            }
            RefreshDeviceGrid();
            deviceInfo.Text = workbenchSensors is null ? "设备档案已保存。双击设备或点击“进入设备”打开工作区。" : "设备已更新，请保存工程以保留修改。";
            if (selectedDeviceId is not null && !profiles.Any(item => item.Id == selectedDeviceId))
            {
                selectedDeviceId = null;
                deviceWorkspaceOpen = false;
                contentTabs.SelectedIndex = 6;
            }
        }
        catch (Exception ex) { deviceInfo.Text = $"保存失败：{ex.Message}"; }
        UpdateDeviceConnectionState();
    }

    private void OpenSelectedDevice()
    {
        var profile = SelectedDevice();
        if (profile is null) { deviceInfo.Text = "请先选择设备档案。"; return; }
        OpenDeviceWorkspace(profile);
    }

    private void OpenDeviceWorkspace(DeviceProfile profile)
    {
        if ((isConnecting || client.IsOpen) && selectedDeviceId != profile.Id)
        {
            deviceInfo.Text = "当前设备仍在连接。请点击“返回当前设备”，断开以太网后再切换设备；串口不受影响。";
            return;
        }
        try
        {
            if (selectedDeviceId != profile.Id)
            {
                CaptureActiveWorkbenchConnection();
                var sensors = ReadDeviceSensors(profile.Id);
                Rs485ConfigurationProtocol.Validate(sensors);
                if (selectedDeviceId is not null)
                    SaveRs485Configuration(ReadRs485ConfigurationFromGrid());
                var engineering = ReadEngineeringForDevice(profile.Id);
                selectedDeviceId = profile.Id;
                ClearEngineeringContext();
                currentEngineering = engineering;
                rs485Grid.Rows.Clear();
                foreach (var sensor in sensors) AddRs485GridRow(sensor);
                foreach (DataGridViewRow row in dataGrid.Rows) row.Cells[2].Value = "--";
                lastPollLabel.Text = "最近接收：--";
                for (var index = 0; index < inputIndicators.Length; index++)
                {
                    inputIndicators[index].Text = $"IN{index + 1} --";
                    outputIndicators[index].Text = $"{OutputDisplayNames[index]} --";
                    inputIndicators[index].BackColor = outputIndicators[index].BackColor = UiPalette.Surface;
                    inputIndicators[index].ForeColor = outputIndicators[index].ForeColor = UiPalette.Muted;
                }
                analogVoltageIndicator.Text = "U1 -- V";
                analogCurrentIndicator.Text = "I1 -- mA";
                nextRs485TelemetryPollUtc = DateTime.MinValue;
            }
            if (!client.IsOpen && !isConnecting)
            {
                hostInput.Text = profile.Host;
                ethernetPortInput.Value = profile.Port;
                slaveAddressInput.Value = profile.UnitId;
                pollIntervalInput.Value = profile.PollIntervalMs;
                PrepareTrendForDevice(profile.Host, profile.Port, profile.UnitId);
            }
            deviceWorkspaceOpen = true;
            MarkWorkbenchDirty();
            contentTabs.SelectedIndex = 0;
            UpdateDeviceConnectionState();
        }
        catch (Exception ex) { deviceInfo.Text = $"无法切换设备：{ex.Message}"; }
    }

    private void UpdateDeviceConnectionState()
    {
        if (deviceConnectButton.IsDisposed) return;
        deviceConnectButton.Enabled = deviceSnapshot is not null;
        var active = deviceSnapshot?.Devices.FirstOrDefault(item => item.Id == selectedDeviceId);
        var matches = active is not null && string.Equals(active.Host, hostInput.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            && active.Port == (int)ethernetPortInput.Value && active.UnitId == (byte)slaveAddressInput.Value;
        UpdateWorkspaceShell();
        if (client.IsOpen)
            deviceInfo.Text = matches
                ? $"已连接：{active!.Name} · {active.Host}:{active.Port} · Unit {active.UnitId}。通用工具始终可用。"
                : $"当前连接：{hostInput.Text.Trim()}:{ethernetPortInput.Value} · Unit {slaveAddressInput.Value}。";
    }

    private void UpdateWorkspaceShell()
    {
        var inside = deviceWorkspaceOpen && selectedDeviceId is not null;
        var boardPage = inside && contentTabs.SelectedIndex is >= 0 and < 4;
        globalNavigation.Visible = true;
        deviceNavigation.Visible = inside;
        deviceHomeButton.Visible = true;
        connectButton.Visible = boardPage;
        connectionSummaryButton.Visible = boardPage;
        connectionBar.Visible = boardPage && connectionExpanded;
        if (connectionDrawer is not null) connectionDrawer.Visible = boardPage && connectionExpanded;
        samplingBar.Visible = boardPage;
        deviceManagerButton.Text = inside ? "返回工作台" : "我的设备";
        workspaceTitle.Text = boardPage ? "DAQ-800 · 多源采集控制器" :
            contentTabs.SelectedIndex == 4 ? "串口" : contentTabs.SelectedIndex == 5 ? "通信测试" : contentTabs.SelectedIndex == 7 ? "CAN 报文调试" : contentTabs.SelectedIndex == 8 ? "通用工具箱" : contentTabs.SelectedIndex == 9 ? "数据误差分析" : "设备工作台";
        var profile = deviceSnapshot?.Devices.FirstOrDefault(item => item.Id == selectedDeviceId);
        deviceNavigationTitle.Text = profile?.Name ?? "DAQ-800 设备工作区";
        deviceNavigationTitle.AutoEllipsis = true;
        activeDeviceLabel.Text = contentTabs.SelectedIndex == 9
            ? "独立分析参考值和测量值，无需连接设备。"
            : boardPage
            ? $"{profile?.Name ?? "DAQ-800"} · {hostInput.Text}:{ethernetPortInput.Value} · Unit {slaveAddressInput.Value} · 目标周期 {pollIntervalInput.Value:0} ms"
            : inside ? $"通用工具 · 当前设备：{profile?.Name ?? "DAQ-800"}" : "选择或创建设备后进入采集工作区；串口、CAN 与网络工具随时可用。";
        if (boardPage)
        {
            headerStatusPill.SetState(isConnecting ? "连接中…" : client.IsOpen ? "已连接" : "未连接",
                isConnecting ? UiPalette.Warning : client.IsOpen ? UiPalette.Green : UiPalette.Muted,
                isConnecting ? UiPalette.WarningSoft : client.IsOpen ? UiPalette.GreenSoft : UiPalette.SurfaceMuted);
        }
        else
        {
            headerStatusPill.SetState(inside ? "工作区" : "设备总览", UiPalette.Muted, UiPalette.SurfaceMuted);
        }
        Text = inside ? $"设备工作台 · {profile?.Name ?? DeviceCatalog.ProductModel}" : "设备工作台";
        headerHost?.PerformLayout();
    }

    private static string DeviceSensorConfigurationPath(Guid id) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IndustrialFieldDataAcquisitionTerminal", "Devices", id.ToString("N"), "rs485-sensors.json");

    private void PrepareTrendForDevice(string host, int port, byte unit)
    {
        var endpoint = $"{selectedDeviceId}/{host.Trim()}:{port}/{unit}";
        if (trendDeviceEndpoint is not null &&
            !string.Equals(trendDeviceEndpoint, endpoint, StringComparison.OrdinalIgnoreCase) && trendRecorder.SnapshotCount > 0)
        {
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
                OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  设备已切换，趋势记录已开始新会话；之前的 CSV 文件保留。");
            }
            catch (Exception ex)
            {
                trendRecordingFailed = true;
                trendStatusLabel.Text = $"设备切换后无法新建趋势记录：{ex.Message}";
                OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  {trendStatusLabel.Text}");
            }
        }
        trendDeviceEndpoint = endpoint;
    }
}

internal sealed class DeviceModelPickerDialog : Form
{
    private readonly Button selectDeviceButton = new();
    private readonly Button nextButton = new();
    private readonly Image productImage = AppBrand.CreateBitmap(64);
    public string? SelectedModel { get; private set; }

    public DeviceModelPickerDialog()
    {
        Text = "创建设备 · 选择产品";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(600, 360);
        Font = new Font("Microsoft YaHei UI", 11F);
        BackColor = UiPalette.Canvas;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(new Label { Text = "选择要创建的设备", Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold), ForeColor = UiPalette.Ink }, 0, 0);
        root.Controls.Add(new Label { Text = "1  选择产品       →       2  填写设备信息", Dock = DockStyle.Fill, ForeColor = UiPalette.Muted }, 0, 1);
        selectDeviceButton.Text = "DAQ-800\n多源采集控制器\n以太网接入 · 板载信号采集 · RS485 · IO 联动";
        selectDeviceButton.Image = productImage;
        selectDeviceButton.ImageAlign = ContentAlignment.MiddleLeft;
        selectDeviceButton.TextAlign = ContentAlignment.MiddleLeft;
        selectDeviceButton.TextImageRelation = TextImageRelation.ImageBeforeText;
        selectDeviceButton.Padding = new Padding(20);
        selectDeviceButton.Dock = DockStyle.Fill;
        selectDeviceButton.Margin = new Padding(0, 0, 0, 12);
        UiPalette.StyleButton(selectDeviceButton);
        selectDeviceButton.Click += (_, _) =>
        {
            SelectedModel = DeviceCatalog.ProductModel;
            selectDeviceButton.BackColor = UiPalette.BlueSoft;
            selectDeviceButton.FlatAppearance.BorderColor = UiPalette.Blue;
            selectDeviceButton.FlatAppearance.BorderSize = 2;
            nextButton.Enabled = true;
            nextButton.Focus();
        };
        root.Controls.Add(selectDeviceButton, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        nextButton.Text = "下一步";
        nextButton.Size = new Size(104, 36);
        nextButton.Enabled = false;
        UiPalette.StyleButton(nextButton, primary: true);
        nextButton.Click += (_, _) => { if (SelectedModel is not null) DialogResult = DialogResult.OK; };
        var cancel = new Button { Text = "取消", Size = new Size(90, 36), DialogResult = DialogResult.Cancel };
        UiPalette.StyleButton(cancel);
        actions.Controls.Add(nextButton);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 3);
        Controls.Add(root);
        UiPalette.ApplyWorkbenchTheme(this);
        AcceptButton = nextButton;
        CancelButton = cancel;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) productImage.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class DeviceProfileDialog : Form
{
    private readonly TextBox name = new() { Dock = DockStyle.Fill };
    private readonly TextBox host = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Dock = DockStyle.Fill };
    private readonly NumericUpDown unit = new() { Minimum = 1, Maximum = 247, Dock = DockStyle.Fill };
    private readonly NumericUpDown interval = new() { Minimum = 100, Maximum = 60000, Dock = DockStyle.Fill };
    private readonly TextBox notes = new() { Dock = DockStyle.Fill, MaxLength = 500 };
    private readonly Guid id;
    public DeviceProfile? Profile { get; private set; }

    public DeviceProfileDialog(DeviceProfile? profile)
    {
        id = profile?.Id ?? Guid.NewGuid();
        Text = profile is null ? "创建设备 · 填写设备信息" : "编辑设备";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(560, 438);
        MinimumSize = Size;
        Font = new Font("Microsoft YaHei UI", 10F);
        BackColor = UiPalette.Surface;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        name.Text = profile?.Name ?? "DAQ-800 · 新设备";
        host.Text = profile?.Host ?? "192.168.1.10";
        port.Value = profile?.Port ?? 5000;
        unit.Value = profile?.UnitId ?? 1;
        interval.Value = profile?.PollIntervalMs ?? 250;
        notes.Text = profile?.Notes ?? "";
        name.MaxLength = 80;
        host.MaxLength = 253;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Control control, int row)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            layout.Controls.Add(control, 1, row);
        }
        Row("产品型号", new Label { Text = "DAQ-800 · 多源采集控制器", Dock = DockStyle.Fill,
            ForeColor = UiPalette.Blue, TextAlign = ContentAlignment.MiddleLeft }, 0);
        Row("设备名称", name, 1); Row("IP / 主机名", host, 2); Row("端口", port, 3);
        Row("Unit ID", unit, 4); Row("轮询间隔 (ms)", interval, 5); Row("备注", notes, 6);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = profile is null ? "创建设备" : "保存", Width = 100, Height = 34 };
        var cancel = new Button { Text = "取消", Width = 80, Height = 34, DialogResult = DialogResult.Cancel };
        UiPalette.StyleButton(save, primary: true);
        UiPalette.StyleButton(cancel);
        save.Click += (_, _) =>
        {
            var candidate = new DeviceProfile(id, name.Text.Trim(), DeviceCatalog.ProductModel, host.Text.Trim(), (int)port.Value,
                (byte)unit.Value, (int)interval.Value, notes.Text.Trim());
            try { DeviceCatalog.Validate(new[] { candidate }); Profile = candidate; DialogResult = DialogResult.OK; }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "设备信息", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 1, 7);
        Controls.Add(layout);
        UiPalette.ApplyWorkbenchTheme(this);
        AcceptButton = save;
        CancelButton = cancel;
    }
}
