using System.Globalization;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private readonly DataGridView toolModbusGrid = new();
    private readonly TextBox toolModbusLog = ToolLog(), modbusBind = new() { Text = "127.0.0.1", Width = 110 }, modbusPort = new() { Text = "1502", Width = 60 };
    private readonly Button modbusStart = ToolButton("启动从站", 100, true);
    private readonly Label modbusStatus = ToolLabel("未启动");
    private Control BuildModbusTcpTool()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 49)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 65)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        var bar = ToolBar(); var add = ToolButton("添加从站", 90); var remove = ToolButton("删除选中", 90); var refresh = ToolButton("刷新数值", 90); var clear = ToolButton("清日志", 75);
        bar.Controls.AddRange(new Control[] { ToolLabel("监听地址"), modbusBind, ToolLabel("端口"), modbusPort, modbusStart, add, remove, refresh, clear, modbusStatus });
        var help = ToolBar(); help.Controls.Add(ToolLabel("默认仅本机访问；地址与寄存器用 HEX，线圈/离散输入用 0 或 1，空格分隔。支持多个 Unit ID。"));
        toolModbusGrid.Dock = DockStyle.Fill; toolModbusGrid.BackgroundColor = UiPalette.Surface; toolModbusGrid.BorderStyle = BorderStyle.None; ConfigureOperationalGridStyle(toolModbusGrid); toolModbusGrid.AllowUserToAddRows = false; toolModbusGrid.AllowUserToDeleteRows = false;
        toolModbusGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; toolModbusGrid.RowHeadersVisible = false; toolModbusGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var title in new[] { "Unit ID (十进制)", "起始地址 HEX", "保持寄存器 HEX", "输入寄存器 HEX", "线圈 0/1", "离散输入 0/1" }) toolModbusGrid.Columns.Add(title, title);
        toolModbusGrid.Rows.Add("1", "0000", "0000 0000 0000 0000", "0000 0000 0000 0000", "0 0 0 0", "0 0 0 0");
        add.Click += (_, _) => { if (toolModbusServer?.Running == true) return; var used = toolModbusGrid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells[0].Value?.ToString()).ToHashSet(); var id = Enumerable.Range(1, 247).FirstOrDefault(i => !used.Contains(i.ToString())); if (id != 0 && toolModbusGrid.Rows.Count < 32) toolModbusGrid.Rows.Add(id.ToString(), "0000", "0000 0000 0000 0000", "0000 0000 0000 0000", "0 0 0 0", "0 0 0 0"); };
        remove.Click += (_, _) => { if (toolModbusServer?.Running != true && toolModbusGrid.SelectedRows.Count > 0) foreach (DataGridViewRow row in toolModbusGrid.SelectedRows) toolModbusGrid.Rows.Remove(row); };
        clear.Click += (_, _) => toolModbusLog.Clear(); refresh.Click += (_, _) => RefreshModbusValues();
        modbusStart.Click += (_, _) =>
        {
            if (toolModbusServer?.Running == true) { RefreshModbusValues(); toolModbusServer.Dispose(); toolModbusServer = null; toolModbusGrid.ReadOnly = false; modbusStart.Text = "启动从站"; modbusStatus.Text = "已停止"; modbusBind.Enabled = modbusPort.Enabled = true; return; }
            try
            {
                toolModbusGrid.EndEdit(); var units = new List<ModbusTcpUnit>();
                foreach (DataGridViewRow row in toolModbusGrid.Rows)
                {
                    string Cell(int i) => row.Cells[i].Value?.ToString()?.Trim() ?? "";
                    if (!byte.TryParse(Cell(0), out var id)) throw new ArgumentException("Unit ID 必须为 1–247 的十进制数。");
                    var start = ParseModbusHexWord(Cell(1));
                    static string[] Tokens(string s) => s.Split(new[] { ' ', ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    ushort[] Words(string s) => Tokens(s).Select(ParseModbusHexWord).ToArray();
                    bool[] Bits(string s) => Tokens(s).Select(v => v == "0" ? false : v == "1" ? true : throw new ArgumentException("位值必须为 0 或 1。")).ToArray();
                    units.Add(new ModbusTcpUnit(id, start, Words(Cell(2)), Words(Cell(3)), Bits(Cell(4)), Bits(Cell(5))));
                }
                var server = new ModbusTcpSimulator(units); server.Log += EnqueueModbusLog;
                try { server.Start(ParseBindAddress(modbusBind.Text), ParsePort(modbusPort.Text, true)); } catch { server.Dispose(); throw; }
                toolModbusUnits = units; toolModbusServer = server; toolModbusGrid.ReadOnly = true; modbusStart.Text = "停止从站"; modbusStatus.Text = "监听 " + server.LocalEndpoint; modbusBind.Enabled = modbusPort.Enabled = false;
            }
            catch (Exception ex) { AppendBounded(toolModbusLog, "启动失败：" + ex.Message); }
        };
        root.Controls.Add(bar, 0, 0); root.Controls.Add(help, 0, 1); root.Controls.Add(toolModbusGrid, 0, 2); root.Controls.Add(toolModbusLog, 0, 3); return root;
    }
    private static ushort ParseModbusHexWord(string text)
    {
        var s = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        if (!ushort.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) throw new ArgumentException("地址和寄存器必须是 0000–FFFF 的十六进制数。"); return value;
    }
    private void RefreshModbusValues()
    {
        if (toolModbusServer?.Running != true) return;
        for (var i = 0; i < toolModbusUnits.Count && i < toolModbusGrid.Rows.Count; i++)
        {
            var snap = toolModbusUnits[i].Snapshot(); var row = toolModbusGrid.Rows[i];
            row.Cells[2].Value = string.Join(" ", snap.Holding.Select(v => v.ToString("X4"))); row.Cells[3].Value = string.Join(" ", snap.Input.Select(v => v.ToString("X4")));
            row.Cells[4].Value = string.Join(" ", snap.Coils.Select(v => v ? "1" : "0")); row.Cells[5].Value = string.Join(" ", snap.Discrete.Select(v => v ? "1" : "0"));
        }
    }
}

