using System.Globalization;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private sealed record ErrorInput(string Reference, string Measured);
    private readonly List<ErrorInput> errorInputs = new();
    private readonly DataGridView errorInputGrid = new();
    private readonly DataGridView errorMetricGrid = new();
    private readonly TextBox errorReference = new();
    private readonly TextBox errorFullScale = new();
    private readonly TextBox errorTypeB = new();
    private readonly TextBox errorCoverage = new();
    private readonly TextBox errorUnit = new();
    private readonly CheckBox errorRepeated = new();
    private readonly Label errorAnalysisStatus = new();
    private readonly Button errorCalculate = new();
    private readonly Button errorImport = new();
    private readonly Button errorExport = new();
    private ErrorAnalysisResult? errorResult;
    private ErrorSample?[]? errorDetails;
    private bool errorBusy;
    private string errorResultUnit = "";

    private Control BuildErrorAnalysisTool()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = UiPalette.Surface };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var actions = ToolBar();
        errorImport.Text = "导入 CSV"; errorImport.Size = new Size(94, 32); UiPalette.StyleButton(errorImport);
        errorImport.Click += async (_, _) => await ImportErrorCsvAsync();
        var add = ToolButton("增加 10 行", 112);
        add.Click += (_, _) =>
        {
            if (errorBusy) return;
            if (errorInputs.Count + 10 > ErrorCsv.MaxRows) { errorAnalysisStatus.Text = $"最多 {ErrorCsv.MaxRows} 行。"; return; }
            for (var i = 0; i < 10; i++) errorInputs.Add(new("", ""));
            errorInputGrid.RowCount = errorInputs.Count;
            InvalidateErrorResult();
        };
        var remove = ToolButton("删除行", 78);
        remove.Click += (_, _) =>
        {
            if (errorBusy || !errorInputGrid.EndEdit()) return;
            var indices = errorInputGrid.SelectedCells.Cast<DataGridViewCell>().Select(cell => cell.RowIndex)
                .Where(index => index >= 0 && index < errorInputs.Count).Distinct().OrderByDescending(index => index).ToArray();
            if (indices.Length == 0 && errorInputGrid.CurrentCell is { RowIndex: >= 0 } current) indices = new[] { current.RowIndex };
            foreach (var index in indices) errorInputs.RemoveAt(index);
            if (errorInputs.Count == 0) errorInputs.Add(new("", ""));
            errorInputGrid.RowCount = errorInputs.Count;
            errorInputGrid.Invalidate();
            InvalidateErrorResult();
            errorAnalysisStatus.Text = indices.Length == 0 ? "请先选择要删除的行。" : $"已删除 {indices.Length} 行。";
        };
        var clear = ToolButton("清空", 66);
        clear.Click += (_, _) =>
        {
            if (errorBusy) return;
            errorInputs.Clear();
            for (var i = 0; i < 10; i++) errorInputs.Add(new("", ""));
            errorInputGrid.RowCount = errorInputs.Count;
            errorInputGrid.Invalidate();
            InvalidateErrorResult();
            errorAnalysisStatus.Text = "已清空；逐行输入参考值和测量值，或导入 CSV。";
        };
        errorCalculate.Text = "计算误差"; errorCalculate.Size = new Size(96, 32); UiPalette.StyleButton(errorCalculate, true);
        errorCalculate.Click += async (_, _) => await CalculateErrorsAsync();
        errorExport.Text = "导出结果 CSV"; errorExport.Size = new Size(145, 32); UiPalette.StyleButton(errorExport);
        errorExport.Enabled = false; errorExport.Click += async (_, _) => await ExportErrorsAsync();
        var help = ToolButton("指标说明", 90);
        help.Click += (_, _) =>
        {
            var manual = Path.Combine(AppContext.BaseDirectory, "Docs", "误差分析说明.txt");
            if (!File.Exists(manual)) { errorAnalysisStatus.Text = "未找到误差分析说明文件。"; return; }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(manual) { UseShellExecute = true }); }
            catch (Exception ex) { errorAnalysisStatus.Text = "打开说明失败：" + ex.Message; }
        };
        actions.Controls.AddRange(new Control[] { errorImport, add, remove, clear, errorCalculate, errorExport, help });
        root.Controls.Add(actions, 0, 0);

        var settings = ToolBar();
        errorReference.Width = 90;
        errorFullScale.Width = 90;
        errorTypeB.Width = 85;
        errorCoverage.Width = 50; errorCoverage.Text = "2";
        errorUnit.Width = 60;
        errorRepeated.Text = "同条件独立重复测量"; errorRepeated.AutoSize = true; errorRepeated.Margin = new Padding(10, 9, 4, 0);
        settings.Controls.AddRange(new Control[] { ToolLabel("统一参考值"), errorReference, ToolLabel("满量程跨度"), errorFullScale,
            ToolLabel("B 类标准不确定度"), errorTypeB, ToolLabel("k"), errorCoverage, ToolLabel("单位"), errorUnit, errorRepeated });
        root.Controls.Add(settings, 0, 1);
        foreach (var input in new[] { errorReference, errorFullScale, errorTypeB, errorCoverage, errorUnit }) input.TextChanged += (_, _) => InvalidateErrorResult();
        errorRepeated.CheckedChanged += (_, _) => InvalidateErrorResult();

        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1000, 500), SplitterDistance = 470, Orientation = Orientation.Vertical, SplitterWidth = 8,
            Panel1MinSize = 260, Panel2MinSize = 260, BackColor = UiPalette.Canvas };
        split.SizeChanged += (_, _) =>
        {
            if (split.Width > split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth &&
                (split.SplitterDistance < split.Panel1MinSize || split.SplitterDistance > split.Width - split.Panel2MinSize - split.SplitterWidth))
                split.SplitterDistance = split.Width / 2;
        };
        ConfigureErrorInputGrid();
        ConfigureErrorMetricGrid();
        split.Panel1.Controls.Add(errorInputGrid);
        split.Panel2.Controls.Add(errorMetricGrid);
        root.Controls.Add(split, 0, 2);
        errorAnalysisStatus.Dock = DockStyle.Fill;
        errorAnalysisStatus.ForeColor = UiPalette.Muted;
        errorAnalysisStatus.Text = "逐行输入同单位的参考值与测量值；参考列留空时使用统一参考值。支持 UTF-8 / GBK CSV 按列导入。";
        root.Controls.Add(errorAnalysisStatus, 0, 3);
        for (var i = 0; i < 10; i++) errorInputs.Add(new("", ""));
        errorInputGrid.RowCount = errorInputs.Count;
        return root;
    }

    private void ConfigureErrorInputGrid()
    {
        errorInputGrid.Dock = DockStyle.Fill;
        errorInputGrid.VirtualMode = true;
        errorInputGrid.AllowUserToAddRows = false;
        errorInputGrid.AllowUserToDeleteRows = false;
        errorInputGrid.AllowUserToResizeRows = false;
        errorInputGrid.RowHeadersVisible = false;
        errorInputGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        errorInputGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "index", HeaderText = "#", Width = 48, ReadOnly = true });
        errorInputGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "reference", HeaderText = "参考值", Width = 110 });
        errorInputGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "measured", HeaderText = "测量值", Width = 110 });
        errorInputGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "error", HeaderText = "有符号误差", Width = 120, ReadOnly = true });
        errorInputGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "absolute", HeaderText = "绝对误差", Width = 110, ReadOnly = true });
        errorInputGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "relative", HeaderText = "相对误差 %", Width = 120, ReadOnly = true });
        errorInputGrid.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= errorInputs.Count) return;
            var row = errorInputs[e.RowIndex];
            var detail = errorDetails is not null && e.RowIndex < errorDetails.Length ? errorDetails[e.RowIndex] : null;
            var difference = detail.HasValue ? detail.Value.Measured - detail.Value.Reference : (double?)null;
            e.Value = e.ColumnIndex switch
            {
                0 => (e.RowIndex + 1).ToString(CultureInfo.InvariantCulture),
                1 => row.Reference,
                2 => row.Measured,
                3 => FormatErrorValue(difference),
                4 => FormatErrorValue(difference.HasValue ? Math.Abs(difference.Value) : null),
                5 => FormatErrorValue(detail.HasValue && detail.Value.Reference != 0 ? difference / detail.Value.Reference * 100 : null),
                _ => ""
            };
        };
        errorInputGrid.CellValuePushed += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= errorInputs.Count) return;
            var value = Convert.ToString(e.Value, CultureInfo.InvariantCulture) ?? "";
            var row = errorInputs[e.RowIndex];
            if (e.ColumnIndex == 1) errorInputs[e.RowIndex] = row with { Reference = value };
            else if (e.ColumnIndex == 2) errorInputs[e.RowIndex] = row with { Measured = value };
            else return;
            InvalidateErrorResult();
        };
        errorInputGrid.DataError += (_, e) => { e.ThrowException = false; };
        UiPalette.ApplyGrid(errorInputGrid);
        errorInputGrid.DefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
        errorInputGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 3, 0);
    }

    private void ConfigureErrorMetricGrid()
    {
        errorMetricGrid.Dock = DockStyle.Fill;
        errorMetricGrid.ReadOnly = true;
        errorMetricGrid.AllowUserToAddRows = false;
        errorMetricGrid.AllowUserToDeleteRows = false;
        errorMetricGrid.AllowUserToResizeRows = false;
        errorMetricGrid.RowHeadersVisible = false;
        errorMetricGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        errorMetricGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "指标", Width = 190 });
        errorMetricGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "结果", Width = 125 });
        errorMetricGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "单位", Width = 76 });
        errorMetricGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "定义 / 适用条件", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 220 });
        UiPalette.ApplyGrid(errorMetricGrid);
        errorMetricGrid.DefaultCellStyle.Padding = new Padding(6, 0, 4, 0);
        errorMetricGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 3, 0);
    }

    private void InvalidateErrorResult()
    {
        if (errorResult is null && errorDetails is null) return;
        errorResult = null; errorDetails = null;
        errorMetricGrid.Rows.Clear();
        errorExport.Enabled = false;
        errorInputGrid.Invalidate();
        errorAnalysisStatus.Text = "数据或参数已修改，请重新计算。";
    }

    private static string FormatErrorValue(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("G9", CultureInfo.InvariantCulture) : "—";

    private void SetErrorBusy(bool busy)
    {
        errorBusy = busy;
        errorCalculate.Enabled = errorImport.Enabled = !busy;
        errorInputGrid.ReadOnly = busy;
        foreach (var input in new[] { errorReference, errorFullScale, errorTypeB, errorCoverage, errorUnit }) input.Enabled = !busy;
        errorRepeated.Enabled = !busy;
        errorExport.Enabled = !busy && errorResult is not null;
    }

    private static double ParseErrorNumber(string text, string label)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new ArgumentException($"{label}不是有效的有限数值，请使用小数点。");
        return value;
    }
    private static double? OptionalErrorNumber(string text, string label) =>
        string.IsNullOrWhiteSpace(text) ? null : ParseErrorNumber(text, label);

    private async Task CalculateErrorsAsync()
    {
        if (errorBusy || !errorInputGrid.EndEdit()) return;
        SetErrorBusy(true);
        try
        {
            var input = errorInputs.ToArray();
            var referenceText = errorReference.Text; var scaleText = errorFullScale.Text;
            var typeBText = errorTypeB.Text; var coverageText = errorCoverage.Text;
            var repeated = errorRepeated.Checked; var unit = errorUnit.Text.Trim();
            var calculated = await Task.Run(() =>
            {
                var constant = OptionalErrorNumber(referenceText, "统一参考值");
                var scale = OptionalErrorNumber(scaleText, "满量程跨度");
                var typeB = OptionalErrorNumber(typeBText, "B 类标准不确定度");
                var k = ParseErrorNumber(coverageText, "包含因子 k");
                var rows = new List<ErrorSample>();
                var details = new ErrorSample?[input.Length];
                for (var i = 0; i < input.Length; i++)
                {
                    var row = input[i];
                    if (string.IsNullOrWhiteSpace(row.Reference) && string.IsNullOrWhiteSpace(row.Measured)) continue;
                    var reference = string.IsNullOrWhiteSpace(row.Reference)
                        ? constant ?? throw new ArgumentException($"第 {i + 1} 行缺少参考值；可填写统一参考值。")
                        : ParseErrorNumber(row.Reference, $"第 {i + 1} 行参考值");
                    var measured = ParseErrorNumber(row.Measured, $"第 {i + 1} 行测量值");
                    var sample = new ErrorSample(reference, measured);
                    rows.Add(sample); details[i] = sample;
                }
                return (Result: ErrorAnalysis.Calculate(rows, scale, repeated, typeB, k), Details: details);
            });
            if (IsDisposed || Disposing) return;
            errorResult = calculated.Result; errorDetails = calculated.Details; errorResultUnit = unit;
            errorMetricGrid.Rows.Clear();
            foreach (var metric in calculated.Result.Metrics)
                errorMetricGrid.Rows.Add(metric.Name, FormatErrorValue(metric.Value), ErrorCsv.MetricUnit(metric, unit), metric.Description);
            errorInputGrid.Invalidate();
            errorAnalysisStatus.Text = $"已计算 {calculated.Result.Samples.Count} 组数据；“—”表示条件不足或不可计算。修改数据后需重新计算。";
        }
        catch (Exception ex) { errorAnalysisStatus.Text = "计算失败：" + ex.Message; }
        finally
        {
            if (!IsDisposed && !Disposing) SetErrorBusy(false);
        }
    }

    private async Task ImportErrorCsvAsync()
    {
        if (errorBusy) return;
        using var dialog = new OpenFileDialog { Title = "选择数据 CSV", Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = dialog.FileName;
        SetErrorBusy(true);
        try
        {
            var rows = await Task.Run(() => ErrorCsv.Read(path));
            if (IsDisposed || Disposing) return;
            using var mapping = new ErrorCsvMappingDialog(rows);
            if (mapping.ShowDialog(this) != DialogResult.OK) return;
            var imported = new List<ErrorInput>();
            foreach (var row in rows.Skip(mapping.HasHeader ? 1 : 0))
            {
                var reference = mapping.ReferenceColumn < 0 || mapping.ReferenceColumn >= row.Length ? "" : row[mapping.ReferenceColumn];
                var measured = mapping.MeasuredColumn >= row.Length ? "" : row[mapping.MeasuredColumn];
                if (string.IsNullOrWhiteSpace(reference) && string.IsNullOrWhiteSpace(measured)) continue;
                imported.Add(new(reference, measured));
            }
            if (imported.Count == 0) throw new InvalidDataException("所选列没有数据。");
            if (imported.Count > ErrorCsv.MaxRows) throw new InvalidDataException($"最多导入 {ErrorCsv.MaxRows} 行。");
            errorInputs.Clear(); errorInputs.AddRange(imported);
            errorInputGrid.RowCount = errorInputs.Count; errorInputGrid.Invalidate();
            InvalidateErrorResult();
            errorAnalysisStatus.Text = $"已导入 {errorInputs.Count} 行；请选择参数后计算。导入不会自动覆盖原 CSV。";
        }
        catch (Exception ex) { errorAnalysisStatus.Text = "导入失败：" + ex.Message; }
        finally { if (!IsDisposed && !Disposing) SetErrorBusy(false); }
    }

    private async Task ExportErrorsAsync()
    {
        if (errorBusy || errorResult is not { } result) return;
        using var dialog = new SaveFileDialog { Title = "导出误差分析", Filter = "CSV 文件 (*.csv)|*.csv", DefaultExt = "csv",
            FileName = $"误差分析_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var options = new ErrorReportOptions(errorReference.Text, errorFullScale.Text, errorTypeB.Text,
            errorCoverage.Text, errorResultUnit, errorRepeated.Checked);
        var path = dialog.FileName;
        SetErrorBusy(true);
        try
        {
            await Task.Run(() => ErrorCsv.WriteReport(path, result, options));
            if (!IsDisposed && !Disposing) errorAnalysisStatus.Text = $"已导出 {result.Samples.Count} 组数据和全部指标。";
        }
        catch (Exception ex) { if (!IsDisposed && !Disposing) errorAnalysisStatus.Text = "导出失败：" + ex.Message; }
        finally { if (!IsDisposed && !Disposing) SetErrorBusy(false); }
    }
}

internal sealed class ErrorCsvMappingDialog : Form
{
    private readonly ComboBox reference = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox measured = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly CheckBox header = new() { Text = "首行是列名", Checked = true, AutoSize = true };
    public bool HasHeader => header.Checked;
    public int ReferenceColumn => reference.SelectedIndex - 1;
    public int MeasuredColumn => measured.SelectedIndex;

    public ErrorCsvMappingDialog(IReadOnlyList<string[]> rows)
    {
        Text = "选择 CSV 数据列"; StartPosition = FormStartPosition.CenterParent;
        Size = new Size(490, 260); MinimumSize = new Size(490, 260); Font = UiPalette.BodyFont; BackColor = UiPalette.Surface;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(16) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        var count = rows.Max(row => row.Length);
        if (count == 0) throw new InvalidDataException("CSV 没有列。");
        void RefreshColumns()
        {
            var oldRef = reference.SelectedIndex; var oldMeasured = measured.SelectedIndex;
            reference.Items.Clear(); measured.Items.Clear(); reference.Items.Add("使用统一参考值");
            for (var i = 0; i < count; i++)
            {
                var name = header.Checked && i < rows[0].Length ? rows[0][i].Trim() : "";
                var label = $"第 {i + 1} 列" + (name.Length > 0 ? $" · {name}" : "");
                reference.Items.Add(label); measured.Items.Add(label);
            }
            reference.SelectedIndex = oldRef >= 0 && oldRef < reference.Items.Count ? oldRef : count == 1 ? 0 : 1;
            measured.SelectedIndex = oldMeasured >= 0 && oldMeasured < measured.Items.Count ? oldMeasured : Math.Min(1, count - 1);
        }
        header.CheckedChanged += (_, _) => RefreshColumns();
        RefreshColumns();
        root.Controls.Add(header, 1, 0);
        root.Controls.Add(new Label { Text = "参考值", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1); root.Controls.Add(reference, 1, 1);
        root.Controls.Add(new Label { Text = "测量值", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2); root.Controls.Add(measured, 1, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "导入", Width = 80, Height = 30, DialogResult = DialogResult.OK }; UiPalette.StyleButton(ok, true);
        var cancel = new Button { Text = "取消", Width = 80, Height = 30, DialogResult = DialogResult.Cancel }; UiPalette.StyleButton(cancel);
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); root.Controls.Add(buttons, 1, 3);
        Controls.Add(root); UiPalette.ApplyWorkbenchTheme(this); AcceptButton = ok; CancelButton = cancel;
    }
}
