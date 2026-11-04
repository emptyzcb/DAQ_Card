namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private Control BuildDataConversionTool()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 }; type.Items.AddRange(new object[] { "UInt16", "Int16", "UInt32", "Int32", "Float32", "UInt64", "Int64", "Float64" }); type.SelectedIndex = 4;
        var little = new CheckBox { Text = "小端字节序", AutoSize = true, Margin = new Padding(10, 8, 5, 0) }; var words = new CheckBox { Text = "交换 16 位字顺序", AutoSize = true, Margin = new Padding(10, 8, 5, 0) };
        var settings = ToolBar(); settings.Controls.AddRange(new Control[] { ToolLabel("类型"), type, little, words });
        var hex = new TextBox { Width = 440, Text = "3F 80 00 00" }; var numeric = new TextBox { Width = 260, Text = "1" };
        var decode = ToolButton("HEX → 数值", 110, true); var encode = ToolButton("数值 → HEX", 110, true);
        var a = ToolBar(); a.Controls.AddRange(new Control[] { ToolLabel("十六进制字节"), hex, decode });
        var b = ToolBar(); b.Controls.AddRange(new Control[] { ToolLabel("数值"), numeric, encode });
        var result = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = UiPalette.CodeBackground, ForeColor = UiPalette.CodeText };
        void ConvertValue(bool fromHex)
        {
            try { var bytes = fromHex ? DataConversion.ParseHex(hex.Text) : DataConversion.Encode(numeric.Text.Trim(), type.Text, little.Checked, words.Checked); if (fromHex) numeric.Text = DataConversion.Decode(bytes, type.Text, little.Checked, words.Checked); else hex.Text = string.Join(" ", bytes.Select(x => x.ToString("X2"))); result.Text = "字节：" + string.Join(" ", bytes.Select(x => x.ToString("X2"))) + Environment.NewLine + "二进制：" + string.Join(" ", bytes.Select(x => Convert.ToString(x, 2).PadLeft(8, '0'))) + Environment.NewLine + "结果：" + numeric.Text; }
            catch (Exception ex) { result.Text = "转换失败：" + ex.Message; }
        }
        decode.Click += (_, _) => ConvertValue(true); encode.Click += (_, _) => ConvertValue(false);
        root.Controls.Add(settings, 0, 0); root.Controls.Add(a, 0, 1); root.Controls.Add(b, 0, 2);
        root.Controls.Add(ToolLabel("小端：整个数值字节倒序；交换 16 位字：反转寄存器顺序。数值使用小数点。"), 0, 3); root.Controls.Add(result, 0, 4); return root;
    }
}
