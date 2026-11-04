using System.Globalization;
using System.Text.Json;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private Dictionary<Guid, List<EngineeringChannel>>? workbenchEngineering;
    private List<EngineeringChannel> currentEngineering = new();
    private List<Rs485SensorConfig>? verifiedEngineeringRs485;

    private List<EngineeringChannel> ReadEngineeringForDevice(Guid id)
    {
        if (workbenchSensors is not null)
            return workbenchEngineering?.GetValueOrDefault(id)?.ToList() ?? new();
        var path = Path.Combine(Path.GetDirectoryName(DeviceSensorConfigurationPath(id))!, "engineering.json");
        if (!File.Exists(path)) return new();
        var items = JsonSerializer.Deserialize<List<EngineeringChannel>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("工程量配置文件无效，请检查原文件。");
        EngineeringChannel.Validate(items);
        return items;
    }

    private void SaveEngineeringForDevice(Guid id, List<EngineeringChannel> items)
    {
        EngineeringChannel.Validate(items);
        if (workbenchSensors is not null)
        {
            workbenchEngineering ??= new();
            workbenchEngineering[id] = items.ToList();
            MarkWorkbenchDirty();
            return;
        }
        var path = Path.Combine(Path.GetDirectoryName(DeviceSensorConfigurationPath(id))!, "engineering.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private void ClearEngineeringContext()
    {
        verifiedEngineeringRs485 = null;
        latestSnapshotForDisplay = null;
        latestRs485ForDisplay = null;
        pendingSnapshotDisplay = null;
        currentEngineering = new();
        trendRecorder.SetEngineeringChannels(new Dictionary<string, EngineeringChannel>());
    }

    private Dictionary<string, EngineeringChannel> EffectiveEngineeringChannels()
    {
        var result = currentEngineering.Where(item => item.Source.StartsWith("AD:", StringComparison.Ordinal))
            .ToDictionary(item => item.Source, StringComparer.Ordinal);
        if (verifiedEngineeringRs485 is null) return result;
        IReadOnlyList<Rs485SensorConfig> current;
        try { current = ReadRs485ConfigurationFromGrid(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException) { return result; }
        foreach (var item in currentEngineering.Where(item => item.Source.StartsWith("RS:", StringComparison.Ordinal)))
        {
            var parts = item.Source.Split(':');
            if (parts.Length != 6 || !int.TryParse(parts[1], out var index) || index >= current.Count || index >= verifiedEngineeringRs485.Count ||
                !int.TryParse(parts[5], out var offset)) continue;
            var sensor = current[index]; var verified = verifiedEngineeringRs485[index];
            if (!sensor.Enabled || !verified.Enabled || sensor.SlaveAddress != verified.SlaveAddress ||
                sensor.Function != verified.Function || sensor.StartRegister != verified.StartRegister || sensor.Quantity != verified.Quantity ||
                offset < 0 || offset >= sensor.Quantity || item.Source != EngineeringChannel.RsSource(index, sensor.SlaveAddress, sensor.Function, sensor.StartRegister, offset)) continue;
            result[$"RS:{index}:{sensor.SlaveAddress}:{sensor.Function:X2}:{offset}"] = item;
        }
        return result;
    }

    private static string FormatRs485Engineering(IReadOnlyDictionary<string, EngineeringChannel> effective, int index, Rs485SensorTelemetry item, int offset, ushort value)
    {
        var source = $"RS:{index}:{item.SlaveAddress}:{item.Function:X2}:{offset}";
        return effective.TryGetValue(source, out var config)
            ? $"{config.Name}: {EngineeringChannel.Format(config.Convert(value))} {config.Unit} (原始 {value})"
            : $"{value} [0x{value:X4}]";
    }

    private void EditEngineeringChannels()
    {
        if (selectedDeviceId is not Guid id) { SetStatus("请先进入设备，再配置工程量。"); return; }
        try
        {
            if (!rs485Grid.EndEdit()) throw new InvalidOperationException("请先完成 RS485 表格编辑。");
            var sensors = ReadRs485ConfigurationFromGrid();
            using var dialog = new EngineeringDialog(currentEngineering, sensors);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null) return;
            var updated = dialog.Result;
            if (updated.SequenceEqual(currentEngineering)) return;
            // A new recording keeps each curve and CSV segment tied to one conversion definition.
            SaveEngineeringForDevice(id, updated);
            currentEngineering = updated;
            trendRecorder.StartNewSession();
            trendRecordingFailed = trendPaused = false;
            frozenTrendPoints = null; frozenTrendChannels = null;
            trendPauseButton.Text = "暂停查看";
            trendExportButton.Enabled = false;
            ResetTrendView();
            RefreshTrendChannels();
            UpdateTrendStatus();
            SetStatus("工程量已保存；已开始新的趋势记录。RS485 换算需先下载对应采集表。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "工程量配置", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

internal sealed class EngineeringDialog : Form
{
    private readonly DataGridView grid = new();
    public List<EngineeringChannel>? Result { get; private set; }
    public EngineeringDialog(IReadOnlyList<EngineeringChannel> existing, IReadOnlyList<Rs485SensorConfig> sensors)
    {
        Text = "工程量配置 · 两点线性换算";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1050, 550); Size = new Size(1250, 700);
        Font = new Font("Microsoft YaHei UI", 9F); BackColor = UiPalette.Surface;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "勾选需要换算的通道，填写两组对应的原始值与工程值（超出两点时线性外推）。\r\nAD7606 使用有符号原始码；RS485 使用单个无符号 16 位寄存器。RS485 需下载当前采集表后才换算。未匹配项可取消勾选删除。", ForeColor = UiPalette.Ink }, 0, 0);
        grid.Dock = DockStyle.Fill; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = UiPalette.Surface; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "use", HeaderText = "启用", FillWeight = 45 });
        foreach (var (name, label, weight) in new[] { ("source", "原始通道", 135), ("name", "名称", 100), ("unit", "单位", 65),
            ("rawLow", "原始点 1", 85), ("valueLow", "工程值 1", 85), ("rawHigh", "原始点 2", 85), ("valueHigh", "工程值 2", 85) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = label, FillWeight = weight, ReadOnly = name == "source" });
        var sources = new List<(string Key, string Label)>();
        for (var i = 0; i < 8; i++) sources.Add(($"AD:{i}", $"AD7606 CH{i + 1}"));
        for (var i = 0; i < sensors.Count; i++)
            for (var offset = 0; offset < sensors[i].Quantity; offset++)
            {
                var sensor = sensors[i];
                sources.Add((EngineeringChannel.RsSource(i, sensor.SlaveAddress, sensor.Function, sensor.StartRegister, offset),
                    $"{sensor.Name} · {sensor.SlaveAddress}/{sensor.Function:X2} · 寄存器 {sensor.StartRegister + offset}"));
            }
        foreach (var obsolete in existing.Where(c => !sources.Any(s => s.Key == c.Source)))
            sources.Add((obsolete.Source, "未匹配 · " + obsolete.Source));
        foreach (var (key, label) in sources)
        {
            var c = existing.FirstOrDefault(x => x.Source == key);
            var index = grid.Rows.Add(c is not null, label, c?.Name ?? label, c?.Unit ?? "", c?.RawLow ?? 0, c?.ValueLow ?? 0, c?.RawHigh ?? 1, c?.ValueHigh ?? 1);
            grid.Rows[index].Tag = key;
        }
        root.Controls.Add(grid, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        var save = new Button { Text = "保存配置", Width = 100, Height = 32 }; UiPalette.StyleButton(save, true);
        var cancel = new Button { Text = "取消", Width = 80, Height = 32, DialogResult = DialogResult.Cancel }; UiPalette.StyleButton(cancel);
        save.Click += (_, _) => Save(); buttons.Controls.Add(save); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 0, 2);
        Controls.Add(root); UiPalette.ApplyWorkbenchTheme(this); AcceptButton = save; CancelButton = cancel;
    }
    private void Save()
    {
        try
        {
            if (!grid.EndEdit()) throw new InvalidOperationException("请先完成当前单元格编辑。");
            var result = new List<EngineeringChannel>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!Convert.ToBoolean(row.Cells["use"].Value)) continue;
                double Number(string name)
                {
                    var text = Convert.ToString(row.Cells[name].Value) ?? "";
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) &&
                        !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                        throw new InvalidDataException($"{row.Cells["source"].Value}：{grid.Columns[name]!.HeaderText}不是有效数字。");
                    return value;
                }
                result.Add(new EngineeringChannel((string)row.Tag!, Convert.ToString(row.Cells["name"].Value)?.Trim() ?? "",
                    Convert.ToString(row.Cells["unit"].Value)?.Trim() ?? "", Number("rawLow"), Number("rawHigh"), Number("valueLow"), Number("valueHigh")));
            }
            EngineeringChannel.Validate(result);
            Result = result; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
