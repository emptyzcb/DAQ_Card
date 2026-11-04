using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private Control BuildRs485Page()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiPalette.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(SectionTitle("RS485 传感器配置"), 0, 0);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(10, 9, 10, 7),
            BackColor = UiPalette.Surface
        };
        ConfigureRs485Button(rs485AddButton, "添加传感器", (_, _) => AddRs485GridRow());
        ConfigureRs485Button(rs485RemoveButton, "删除选中", (_, _) => RemoveSelectedRs485Rows());
        ConfigureRs485Button(rs485UploadButton, "下载配置", async (_, _) => await UploadRs485ConfigurationAsync());
        ConfigureRs485Button(rs485ReadButton, "读取板卡配置", async (_, _) => await ReadRs485ConfigurationAsync());
        toolbar.Controls.AddRange(new Control[]
        {
            rs485AddButton, rs485RemoveButton, rs485UploadButton, rs485ReadButton
        });
        toolbar.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "最多 32 项 · FC03/FC04 · 每项 1~16 个寄存器",
            ForeColor = UiPalette.Muted,
            Margin = new Padding(14, 6, 0, 0)
        });
        root.Controls.Add(toolbar, 0, 1);

        ConfigureRs485Grid();
        var gridHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            BackColor = UiPalette.Surface
        };
        gridHost.Controls.Add(rs485Grid);
        root.Controls.Add(gridHost, 0, 2);
        return root;
    }

    private static void ConfigureRs485Button(Button button, string text, EventHandler handler)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Height = 29;
        button.Margin = new Padding(0, 0, 6, 0);
        UiPalette.StyleButton(button, text == "下载配置");
        button.Click += handler;
    }

    private void ConfigureRs485Grid()
    {
        rs485Grid.Dock = DockStyle.Fill;
        rs485Grid.AllowUserToAddRows = false;
        rs485Grid.AllowUserToDeleteRows = false;
        rs485Grid.AllowUserToResizeRows = false;
        rs485Grid.RowHeadersVisible = false;
        rs485Grid.MultiSelect = true;
        rs485Grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        rs485Grid.BackgroundColor = UiPalette.Surface;
        rs485Grid.BorderStyle = BorderStyle.None;
        rs485Grid.GridColor = UiPalette.Border;
        ConfigureOperationalGridStyle(rs485Grid);
        rs485Grid.EnableHeadersVisualStyles = false;
        rs485Grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiPalette.Canvas,
            ForeColor = UiPalette.Ink,
            Font = new Font(Font, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0)
        };
        rs485Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "index", HeaderText = "#", Width = 42, ReadOnly = true
        });
        rs485Grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "enabled", HeaderText = "启用", Width = 54
        });
        rs485Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "sensorName", HeaderText = "名称", Width = 140
        });
        rs485Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "slave", HeaderText = "从站", Width = 60
        });
        var functionColumn = new DataGridViewComboBoxColumn
        {
            Name = "function", HeaderText = "功能码", Width = 118,
            FlatStyle = FlatStyle.Flat
        };
        functionColumn.Items.AddRange("03 保持寄存器", "04 输入寄存器");
        rs485Grid.Columns.Add(functionColumn);
        rs485Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "start", HeaderText = "起始寄存器", Width = 104
        });
        rs485Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "quantity", HeaderText = "数量", Width = 58
        });
        rs485Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "interval", HeaderText = "周期(ms)", Width = 82
        });
        rs485Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "state", HeaderText = "采集状态", Width = 96, ReadOnly = true
        });
        rs485Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "values", HeaderText = "最新数据", ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 180
        });
        rs485Grid.DataError += (_, _) => { };
    }

    private void AddRs485GridRow(Rs485SensorConfig? config = null)
    {
        if (rs485Grid.Rows.Count >= Rs485ConfigurationProtocol.MaximumItems)
        {
            SetStatus("RS485 采集项已达 32 项上限");
            return;
        }

        config ??= new Rs485SensorConfig(
            $"传感器 {rs485Grid.Rows.Count + 1}", true, 1, 0x04, 0, 1, 500);
        rs485Grid.Rows.Add(
            rs485Grid.Rows.Count + 1,
            config.Enabled,
            config.Name,
            config.SlaveAddress,
            config.Function == 0x03 ? "03 保持寄存器" : "04 输入寄存器",
            $"0x{config.StartRegister:X4}",
            config.Quantity,
            config.IntervalMs,
            "未下载",
            "--");
    }

    private void RemoveSelectedRs485Rows()
    {
        foreach (DataGridViewRow row in rs485Grid.SelectedRows.Cast<DataGridViewRow>()
                     .OrderByDescending(row => row.Index))
        {
            if (!row.IsNewRow)
            {
                rs485Grid.Rows.RemoveAt(row.Index);
            }
        }
        RenumberRs485Rows();
    }

    private void RenumberRs485Rows()
    {
        for (var index = 0; index < rs485Grid.Rows.Count; index++)
        {
            rs485Grid.Rows[index].Cells["index"].Value = index + 1;
        }
    }

    private IReadOnlyList<Rs485SensorConfig> ReadRs485ConfigurationFromGrid()
    {
        rs485Grid.EndEdit();
        var entries = new List<Rs485SensorConfig>(rs485Grid.Rows.Count);
        for (var index = 0; index < rs485Grid.Rows.Count; index++)
        {
            var row = rs485Grid.Rows[index];
            var name = Convert.ToString(row.Cells["sensorName"].Value)?.Trim();
            var enabled = Convert.ToBoolean(row.Cells["enabled"].Value ?? false);
            var slave = ParseUShortCell(row, "slave", index, "从站地址");
            var functionText = Convert.ToString(row.Cells["function"].Value) ?? string.Empty;
            var function = functionText.TrimStart().StartsWith("03", StringComparison.Ordinal) ? 0x03 :
                           functionText.TrimStart().StartsWith("04", StringComparison.Ordinal) ? 0x04 : -1;
            if (function < 0)
            {
                throw new ArgumentException($"第 {index + 1} 项功能码必须选择 03 或 04。");
            }

            entries.Add(new Rs485SensorConfig(
                string.IsNullOrWhiteSpace(name) ? $"传感器 {index + 1}" : name,
                enabled,
                checked((byte)slave),
                checked((byte)function),
                ParseUShortCell(row, "start", index, "起始寄存器"),
                ParseUShortCell(row, "quantity", index, "采集数量"),
                ParseUShortCell(row, "interval", index, "轮询周期")));
        }

        Rs485ConfigurationProtocol.Validate(entries);
        return entries;
    }

    private static ushort ParseUShortCell(DataGridViewRow row,
                                          string columnName,
                                          int rowIndex,
                                          string fieldName)
    {
        var text = Convert.ToString(row.Cells[columnName].Value)?.Trim() ?? string.Empty;
        var isHex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var numberText = isHex ? text[2..] : text;
        var style = isHex ? System.Globalization.NumberStyles.AllowHexSpecifier :
                            System.Globalization.NumberStyles.None;
        if (!ushort.TryParse(numberText, style, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            throw new ArgumentException($"第 {rowIndex + 1} 项{fieldName}不是有效的 0~65535 整数。");
        }
        return value;
    }

    private async Task UploadRs485ConfigurationAsync()
    {
        verifiedEngineeringRs485 = null;
        if (!client.IsOpen)
        {
            SetStatus("请先连接以太网");
            return;
        }

        try
        {
            var entries = ReadRs485ConfigurationFromGrid();
            rs485UploadButton.Enabled = false;
            await pollGate.WaitAsync();
            try
            {
                await Task.Run(() => rs485Protocol.Download(entries));
            }
            finally
            {
                pollGate.Release();
            }

            SaveRs485Configuration(entries);
            verifiedEngineeringRs485 = entries.ToList();
            RefreshTrendChannels();
            foreach (DataGridViewRow row in rs485Grid.Rows)
            {
                row.Cells["state"].Value = "等待采集";
            }
            nextRs485TelemetryPollUtc = DateTime.MinValue;
            SetStatus($"RS485 采集表已下载：{entries.Count} 项");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  RS485 采集表已应用：{entries.Count} 项");
        }
        catch (Exception ex)
        {
            SetStatus("RS485 配置下载失败");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  RS485 配置下载失败：{ex.Message}");
        }
        finally
        {
            rs485UploadButton.Enabled = client.IsOpen;
        }
    }

    private async Task ReadRs485ConfigurationAsync()
    {
        if (!client.IsOpen)
        {
            SetStatus("请先连接以太网");
            return;
        }

        try
        {
            rs485ReadButton.Enabled = false;
            await pollGate.WaitAsync();
            IReadOnlyList<Rs485SensorConfig> entries;
            try
            {
                entries = await Task.Run(() => rs485Protocol.ReadStagedConfiguration());
            }
            finally
            {
                pollGate.Release();
            }

            var oldNames = rs485Grid.Rows.Cast<DataGridViewRow>()
                .Select(row => Convert.ToString(row.Cells["sensorName"].Value) ?? string.Empty)
                .ToArray();
            rs485Grid.Rows.Clear();
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (index < oldNames.Length && !string.IsNullOrWhiteSpace(oldNames[index]))
                {
                    entry = entry with { Name = oldNames[index] };
                }
                AddRs485GridRow(entry);
            }
            SaveRs485Configuration(ReadRs485ConfigurationFromGrid());
            nextRs485TelemetryPollUtc = DateTime.MinValue;
            SetStatus($"已读取板卡 RS485 配置：{entries.Count} 项");
        }
        catch (Exception ex)
        {
            SetStatus("读取 RS485 配置失败");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  读取 RS485 配置失败：{ex.Message}");
        }
        finally
        {
            rs485ReadButton.Enabled = client.IsOpen;
        }
    }

    private static string LegacyRs485ConfigurationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IndustrialFieldDataAcquisitionTerminal",
        "rs485-sensors.json");

    private string Rs485ConfigurationPath => selectedDeviceId is Guid id
        ? DeviceSensorConfigurationPath(id) : LegacyRs485ConfigurationPath;

    private void SaveRs485Configuration(IReadOnlyList<Rs485SensorConfig> entries)
    {
        if (workbenchSensors is not null)
        {
            if (selectedDeviceId is Guid deviceId)
            {
                if (!workbenchSensors.TryGetValue(deviceId, out var previous) || !previous.SequenceEqual(entries)) MarkWorkbenchDirty();
                workbenchSensors[deviceId] = entries.ToList();
            }
            return;
        }
        var path = Rs485ConfigurationPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LoadRs485Configuration()
    {
        try
        {
            var path = Rs485ConfigurationPath;
            if (File.Exists(path))
            {
                var entries = JsonSerializer.Deserialize<List<Rs485SensorConfig>>(File.ReadAllText(path));
                if (entries is not null)
                {
                    Rs485ConfigurationProtocol.Validate(entries);
                    foreach (var entry in entries)
                    {
                        AddRs485GridRow(entry);
                    }
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  加载 RS485 本地配置失败：{ex.Message}");
        }

        AddRs485GridRow();
    }

    private void UpdateRs485Telemetry(IReadOnlyList<Rs485SensorTelemetry> telemetry)
    {
        var engineering = EffectiveEngineeringChannels();
        while (rs485Grid.Rows.Count < telemetry.Count)
        {
            var item = telemetry[rs485Grid.Rows.Count];
            AddRs485GridRow(new Rs485SensorConfig(
                $"传感器 {item.Index + 1}", item.Enabled, item.SlaveAddress,
                item.Function, 0, Math.Max((ushort)1, item.Quantity), 500));
        }

        for (var index = 0; index < rs485Grid.Rows.Count; index++)
        {
            var row = rs485Grid.Rows[index];
            if (index >= telemetry.Count)
            {
                if (!Equals(row.Cells["state"].Value, "未在活动表")) row.Cells["state"].Value = "未在活动表";
                if (!Equals(row.Cells["values"].Value, "--")) row.Cells["values"].Value = "--";
                continue;
            }

            var item = telemetry[index];
            var state = !item.Configured ? "等待采集" : !item.Enabled ? "已停用" :
                item.LastOk ? "正常" : $"异常 ({item.ErrorCount})";
            var values = !item.Configured || !item.Enabled || !item.LastOk || item.Values.Length == 0 ? "--" :
                string.Join("  ", item.Values.Select((value, offset) => FormatRs485Engineering(engineering, index, item, offset, value)));
            if (!Equals(row.Cells["state"].Value, state)) row.Cells["state"].Value = state;
            if (!Equals(row.Cells["values"].Value, values)) row.Cells["values"].Value = values;
            const string statusTip = "括号内为累计采集失败次数；当前异常时不显示上一次的旧数值。";
            if (row.Cells["state"].ToolTipText != statusTip) row.Cells["state"].ToolTipText = statusTip;
            var color = !item.Configured || !item.Enabled ? UiPalette.Muted : item.LastOk ?
                Color.FromArgb(20, 112, 72) : Color.FromArgb(190, 72, 62);
            if (row.Cells["state"].Style.ForeColor != color) row.Cells["state"].Style.ForeColor = color;
        }
    }

    private void MarkRs485TelemetryUnavailable()
    {
        latestRs485ForDisplay = null;
        foreach (DataGridViewRow row in rs485Grid.Rows)
        {
            row.Cells["state"].Value = "状态读取失败";
            row.Cells["state"].Style.ForeColor = Color.FromArgb(190, 72, 62);
            row.Cells["values"].Value = "--";
        }
    }

}
