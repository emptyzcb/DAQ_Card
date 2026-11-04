using System.Reflection;
using System.Windows.Forms;

if (args.Length is < 1 or > 3)
{
    throw new ArgumentException("Expected application assembly path, optional screenshot directory and probe flags.");
}
var probeFlags = args.Length == 3 ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();

Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var formType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.MainForm", true)!;
using var form = (Form)Activator.CreateInstance(formType, nonPublic: true)!;
form.Show();
Application.DoEvents();

// The shell now starts at the product list. Existing layout checks exercise a selected device.
formType.GetField("selectedDeviceId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, Guid.NewGuid());
formType.GetField("deviceWorkspaceOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
((TabControl)formType.GetField("contentTabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).SelectedIndex = 2;
Application.DoEvents();

var shellCapture = args.Length == 2 ? args[1] : Environment.GetEnvironmentVariable("UI_PROBE_SHELL_CAPTURE");
if (!string.IsNullOrWhiteSpace(shellCapture))
{
    var shellTabs = (TabControl)formType.GetField("contentTabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    form.Size = new System.Drawing.Size(1440, 900);
    for (var index = 0; index < shellTabs.TabCount; index++)
    {
        if (index < 4) formType.GetField("deviceWorkspaceOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
        shellTabs.SelectedIndex = index;
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(Path.Combine(shellCapture, $"shell-{index}.png"));
    }
    form.Size = new System.Drawing.Size(1100, 680);
    foreach (var index in new[] { 0, 2, 4 })
    {
        formType.GetField("deviceWorkspaceOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
        shellTabs.SelectedIndex = index;
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(Path.Combine(shellCapture, $"shell-min-{index}.png"));
    }
    Console.WriteLine("Shell screenshots captured: " + shellCapture);
    form.Close();
    return;
}

if (Environment.GetEnvironmentVariable("UI_PROBE_LOG_LOAD") == "1")
{
    var log = formType.GetMethod("OnClientLog", BindingFlags.Instance | BindingFlags.NonPublic)!;
    const int count = 2400;
    Task.Run(() =>
    {
        for (var index = 0; index < count; index++)
        {
            var direction = index % 2 == 0 ? "TX" : "RX";
            log.Invoke(form, new object[] { $"12:00:00.000  {direction}  TID=0x{index:X4} UNIT=1 PDU=03 02 00 02" });
        }
    }).GetAwaiter().GetResult();

    var markerReached = false;
    form.BeginInvoke((Action)(() => markerReached = true));
    var watch = System.Diagnostics.Stopwatch.StartNew();
    while (!markerReached && watch.Elapsed < TimeSpan.FromSeconds(10))
    {
        Application.DoEvents();
        Thread.Sleep(5);
    }
    if (!markerReached || watch.Elapsed > TimeSpan.FromSeconds(2))
        throw new InvalidOperationException($"Communication log blocked the UI for {watch.ElapsedMilliseconds} ms.");

    var logBox = (RichTextBox)formType.GetField("logBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    watch.Restart();
    while (!logBox.Text.Contains($"TID=0x{count - 1:X4}", StringComparison.Ordinal) &&
           watch.Elapsed < TimeSpan.FromSeconds(10))
    {
        Application.DoEvents();
        Thread.Sleep(5);
    }
    if (!logBox.Text.Contains($"TID=0x{count - 1:X4}", StringComparison.Ordinal))
        throw new InvalidOperationException("Communication log lost its latest entry.");
    Console.WriteLine("Communication log remains responsive under sustained traffic.");
    return;
}

if (Environment.GetEnvironmentVariable("UI_PROBE_TREND_CAPTURE") is { Length: > 0 } fullTrendCapture)
{
    form.Size = new System.Drawing.Size(1500, 860);
    var snapshotType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.AcquisitionSnapshot", true)!;
    var started = DateTime.Now.AddSeconds(-10);
    for (var index = 0; index < 100; index++)
    {
        var snapshot = Activator.CreateInstance(snapshotType, nonPublic: true)!;
        var setpoint = 12.0 + 8.0 * Math.Sin(index * Math.PI / 20.0);
        var measured = 11.8 + 7.7 * Math.Sin((index - 2) * Math.PI / 20.0);
        snapshotType.GetProperty("Timestamp")!.SetValue(snapshot, started.AddMilliseconds(index * 100));
        snapshotType.GetProperty("Status")!.SetValue(snapshot, (ushort)8);
        snapshotType.GetProperty("AnalogCurrentUa")!.SetValue(snapshot, (ushort)(setpoint * 1000));
        snapshotType.GetProperty("Ad7606Raw")!.SetValue(snapshot,
            new short[] { 0, (short)(measured * 155 * 32768 / 10000), 0, 0, 0, 0, 0, 0 });
        formType.GetMethod("UpdateSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new[] { snapshot });
    }
    var tabsForCapture = (TabControl)formType.GetField("contentTabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    tabsForCapture.SelectedIndex = 3;
    Application.DoEvents();
    using var capture = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(capture, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
    capture.Save(fullTrendCapture);
    Console.WriteLine("Trend screenshot captured: " + fullTrendCapture);
    return;
}

if (Environment.GetEnvironmentVariable("UI_PROBE_CONNECT") == "1")
{
    var connectButton = (Button)formType.GetField("connectButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    var connectOperation = formType.GetField("openConnection", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    connectOperation.SetValue(form, new Func<string, int, byte, CancellationToken, Task>((_, _, _, token) =>
    {
        token.Register(() => pending.TrySetCanceled(token));
        return pending.Task;
    }));
    var clickWatch = System.Diagnostics.Stopwatch.StartNew();
    connectButton.PerformClick();
    clickWatch.Stop();
    if (clickWatch.Elapsed > TimeSpan.FromMilliseconds(300) || connectButton.Text != "取消连接")
        throw new InvalidOperationException("A pending Ethernet connection blocked the window.");
    connectButton.PerformClick();
    var cancelWatch = System.Diagnostics.Stopwatch.StartNew();
    while (cancelWatch.Elapsed < TimeSpan.FromSeconds(2) && connectButton.Text != "连接以太网")
    {
        Application.DoEvents();
        Thread.Sleep(20);
    }
    if (connectButton.Text != "连接以太网" || !connectButton.Enabled || pending.Task.Status != TaskStatus.Canceled)
        throw new InvalidOperationException("Cancelling a pending Ethernet connection did not restore the UI.");
    Console.WriteLine("Ethernet connect UI remains responsive and cancellation restores controls.");
}

var splitField = formType.GetField("workspaceSplit", BindingFlags.Instance | BindingFlags.NonPublic)!;
var split = (SplitContainer)splitField.GetValue(form)!;
var tabs = (TabControl)formType.GetField("contentTabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
var compileSplit = (SplitContainer)formType.GetField("compileSplit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
var compileToggle = (Button)formType.GetField("compileOutputToggleButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
var logToggle = (ToolStripButton)formType.GetField("logToggleButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
var compileStatus = (Label)formType.GetField("logicCompileLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
var sourceTree = (TreeView)formType.GetField("sourceTreeView", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
var compileOutput = (RichTextBox)formType.GetField("compileOutputBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
if (sourceTree.SelectedNode?.Parent?.Text != "示例代码")
{
    throw new InvalidOperationException("The initial project must be named 示例代码.");
}
var sourceNode = sourceTree.SelectedNode;
var exampleRoot = Path.Combine(AppContext.BaseDirectory, "Projects", "示例代码");
var manualPath = Path.Combine(exampleRoot, "编程手册.txt");
var manualNode = sourceTree.Nodes.Cast<TreeNode>()
    .FirstOrDefault(node => node.Text == "示例代码")?.Nodes.Cast<TreeNode>()
    .FirstOrDefault(node => node.Text == "编程手册.txt");
if (!File.Exists(manualPath) || manualNode is null)
{
    throw new InvalidOperationException("Example project must contain a visible programming manual.");
}
sourceTree.SelectedNode = manualNode;
Application.DoEvents();
var codeEditor = (RichTextBox)formType.GetField("codeEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
if (!codeEditor.ReadOnly || !codeEditor.Text.Contains("IO_Logic_Main", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Programming manual must open read-only in the project tree.");
}
sourceTree.SelectedNode = sourceNode;

var manualGenerator = formType.GetMethod("EnsureProjectManual", BindingFlags.Static | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("Project manual generation method is missing.");
var probeRoot = Path.Combine(Path.GetTempPath(), "daq-manual-probe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(probeRoot);
try
{
    manualGenerator.Invoke(null, new object[] { probeRoot });
    var generatedPath = Path.Combine(probeRoot, "编程手册.txt");
    if (!File.Exists(generatedPath) || !File.ReadAllText(generatedPath).Contains("IO_Logic_Main", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("New project manual was not generated.");
    }
    File.WriteAllText(generatedPath, "custom manual");
    manualGenerator.Invoke(null, new object[] { probeRoot });
    if (File.ReadAllText(generatedPath) != "custom manual")
    {
        throw new InvalidOperationException("Existing project manual was overwritten.");
    }
}
finally
{
    var tempRoot = Path.GetFullPath(Path.GetTempPath());
    var target = Path.GetFullPath(probeRoot);
    if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Probe cleanup path escaped the temp directory.");
    }
    Directory.Delete(target, recursive: true);
}
if (!compileStatus.Visible || compileStatus.Width < 100 || string.IsNullOrWhiteSpace(compileStatus.Text))
{
    throw new InvalidOperationException("The logic compile status is not visible in the toolbar.");
}
var dataGrid = (DataGridView)formType.GetField("dataGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
    .SelectMany(child => new[] { child }.Concat(Descendants(child)));
if (Descendants(tabs.TabPages[2]).OfType<GroupBox>().Any())
{
    throw new InvalidOperationException("The logic workspace still contains framed group boxes.");
}
if (dataGrid.Rows.Cast<DataGridViewRow>().Any(row =>
    row.Cells["address"].Value?.ToString() is string address &&
    (address.StartsWith("0x002", StringComparison.Ordinal) || address.StartsWith("0x003", StringComparison.Ordinal))))
{
    throw new InvalidOperationException("Removed IMU and attitude registers are still shown in the monitor.");
}
if (tabs.SelectedIndex != 2 || !split.Panel2Collapsed || logToggle.Visible || !compileSplit.Panel2Collapsed)
{
    throw new InvalidOperationException("The logic page must hide the global log and start with compile results collapsed.");
}

compileToggle.PerformClick();
Application.DoEvents();
if (compileSplit.Panel2Collapsed || compileToggle.Text != "收起结果")
{
    throw new InvalidOperationException("Compile results did not expand on request.");
}
compileToggle.PerformClick();
formType.GetMethod("DownloadCProgramAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
Application.DoEvents();
if (compileSplit.Panel2Collapsed || !compileOutput.Text.Contains("下载失败：请先连接以太网。", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Download failure must be visible in compile results.");
}
compileToggle.PerformClick();
tabs.SelectedIndex = 0;
Application.DoEvents();
if (split.Panel2Collapsed || !logToggle.Visible || split.Panel2.Height < 120)
{
    throw new InvalidOperationException("The communication log must return on the monitor page.");
}

Console.WriteLine("UI layout probe passed: logic log hidden, compile results toggle, monitor log restored.");
if (probeFlags.Contains("light-editor"))
{
    foreach (var field in new[] { "codeEditor", "compileOutputBox", "logBox", "simulationLogBox" })
    {
        var control = (Control)formType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        if (control.BackColor.GetBrightness() < 0.8f || control.ForeColor.GetBrightness() > 0.55f)
            throw new InvalidOperationException($"{field} does not match the light workbench palette.");
    }
    foreach (var field in new[] { "lineNumberGutter", "codeMinimap", "findBar", "codeEditorTitle", "codeStatusLabel" })
    {
        var control = (Control)formType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        if (control.BackColor.GetBrightness() < 0.8f)
            throw new InvalidOperationException($"{field} still uses a dark background.");
    }
    Console.WriteLine("Editor and log surfaces use the light workbench palette.");
}
if (Environment.GetEnvironmentVariable("UI_PROBE_VISUAL") == "1" || probeFlags.Contains("visual"))
{
    var sidebar = Descendants(form).SingleOrDefault(control => control.Name == "moduleNavigation")
        ?? throw new InvalidOperationException("Module navigation is missing.");
    var navigationButtons = (Button[])formType.GetField("navigationButtons", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    var activeProperty = navigationButtons[0].GetType().GetProperty("Active", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    if (tabs.ItemSize.Height > 2 || navigationButtons.Length + 1 != tabs.TabCount ||
        !sidebar.Controls.Cast<Control>().Any() || !(bool)activeProperty.GetValue(navigationButtons[0])!)
        throw new InvalidOperationException("Sidebar navigation or selected state is inconsistent.");
    var deviceManager = (Button)formType.GetField("deviceManagerButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    deviceManager.PerformClick();
    Application.DoEvents();
    if (tabs.SelectedIndex != 6 || deviceManager.BackColor != Color.FromArgb(237, 240, 254))
        throw new InvalidOperationException("Global device management entry does not open or indicate its page.");
    formType.GetField("deviceWorkspaceOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
    tabs.SelectedIndex = 0;
    if (tabs.TabPages[0].Controls.Cast<Control>().Single() is not TableLayoutPanel)
        throw new InvalidOperationException("Monitor heading and last-sample line do not have dedicated layout rows.");
    if (dataGrid.RowTemplate.Height < 27 || dataGrid.ColumnHeadersHeight < 30 ||
        dataGrid.DefaultCellStyle.SelectionBackColor.GetBrightness() < 0.8f)
        throw new InvalidOperationException("Monitor table remains cramped or uses a saturated selection color.");
    var rsGrid = (DataGridView)formType.GetField("rs485Grid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    if (rsGrid.ColumnHeadersHeight < 30 || rsGrid.DefaultCellStyle.SelectionBackColor.GetBrightness() < 0.8f)
        throw new InvalidOperationException("RS485 table does not share the quieter table styling.");
    form.Size = new System.Drawing.Size(1020, 650);
    tabs.SelectedIndex = 0;
    Application.DoEvents();
    if (split.Panel2Collapsed || split.Panel2.Height < 120 ||
        split.Panel2.Bounds.Bottom > split.ClientSize.Height ||
        split.PointToScreen(new System.Drawing.Point(0, split.Height)).Y >
        form.PointToScreen(new System.Drawing.Point(0, form.ClientSize.Height)).Y)
        throw new InvalidOperationException($"Communication log disappeared at minimum window size: " +
            $"split={split.Size}, panel2={split.Panel2.Bounds}, form={form.ClientSize}.");
    var heading = Descendants(tabs.TabPages[0]).OfType<Label>().Single(label => label.Text == "实时采集数据");
    var lastPoll = (Label)formType.GetField("lastPollLabel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    if (heading.Height < 30 || lastPoll.Height < 24 ||
        dataGrid.PointToScreen(System.Drawing.Point.Empty).Y <=
        lastPoll.PointToScreen(new System.Drawing.Point(0, lastPoll.Height)).Y)
        throw new InvalidOperationException("Monitor header overlaps its data grid at minimum window size.");
    Console.WriteLine("Visual layout passed: tabs, both tables, monitor heading, minimum window.");
}
if (Environment.GetEnvironmentVariable("UI_PROBE_HEADER") == "1" || probeFlags.Contains("header"))
{
    var header = Descendants(form).SingleOrDefault(control => control.Name == "connectionHeader")
        ?? throw new InvalidOperationException("The colored connection header is missing.");
    using var image = new Bitmap(header.Width, header.Height);
    header.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height));
    var center = image.GetPixel((int)(image.Width * 0.30), 9);
    if (center.R < 245 || center.G < 245 || center.B < 245)
        throw new InvalidOperationException($"The connection header is not a calm white surface: {center}.");
    var title = (Label)formType.GetField("workspaceTitle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    if (string.IsNullOrWhiteSpace(title.Text)) throw new InvalidOperationException("The header title is empty.");
    if (title.ForeColor.GetBrightness() > 0.55f)
        throw new InvalidOperationException("The title lacks contrast on the light header.");
    var connect = (Button)formType.GetField("connectButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    var summary = (Button)formType.GetField("connectionSummaryButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    var details = (FlowLayoutPanel)formType.GetField("connectionBar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    if (details.Visible || !summary.Visible)
        throw new InvalidOperationException("Ethernet settings should start collapsed.");
    var connectInsideHeader = false;
    for (Control? walk = connect; walk is not null; walk = walk.Parent) if (walk == header) connectInsideHeader = true;
    if (!connectInsideHeader || !connect.Visible || connect.Text != "连接以太网" ||
        connect.Left <= summary.Right || connect.Right > header.ClientSize.Width)
        throw new InvalidOperationException("Ethernet connect action is not prominent in the collapsed header.");
    summary.PerformClick();
    Application.DoEvents();
    if (!details.Visible)
        throw new InvalidOperationException("Ethernet settings did not expand.");
    var drawer = (Control)formType.GetField("connectionDrawer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    if (!drawer.Visible) throw new InvalidOperationException("The network drawer card must appear with the settings.");
    summary.PerformClick();
    Application.DoEvents();
    if (details.Visible || drawer.Visible)
        throw new InvalidOperationException("Ethernet settings did not collapse.");
    if (connect.BackColor != Color.FromArgb(62, 99, 221))
        throw new InvalidOperationException("The primary connection action does not use the shared accent.");
    if (Environment.GetEnvironmentVariable("UI_PROBE_HEADER_CAPTURE") is { Length: > 0 } headerCapture)
        image.Save(headerCapture);
    Console.WriteLine("Connection header color and contrast passed.");
}
if (probeFlags.Contains("serial"))
{
    if (tabs.TabCount != 9 || tabs.TabPages[4].Text != "串口" || tabs.TabPages[5].Text != "测试")
        throw new InvalidOperationException("Serial and test navigation pages are missing.");
    var baud = (ComboBox)formType.GetField("serialBaudInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    if (!baud.Items.Cast<object>().Any(item => int.TryParse(item.ToString(), out var value) && value > 115200))
        throw new InvalidOperationException("High baud rates are missing.");
    var simulation = formType.GetField("modbusSimulator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form);
    if (simulation is null)
        throw new InvalidOperationException("Modbus RTU simulator is missing.");
    tabs.SelectedIndex = 4;
    var queue = formType.GetMethod("QueueSerialTraffic", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var flush = formType.GetMethod("FlushSerialTraffic", BindingFlags.Instance | BindingFlags.NonPublic)!;
    queue.Invoke(form, new object[] { "RX", System.Text.Encoding.UTF8.GetBytes("1.0,2.0\n1.5,2.5\n"), null! });
    flush.Invoke(form, null);
    var genericChannels = Descendants(tabs.TabPages[4]).OfType<CheckBox>()
        .Where(check => check.Text.StartsWith("CH", StringComparison.Ordinal)).ToArray();
    if (genericChannels.Length != 2 || genericChannels[0].Text != "CH1" ||
        genericChannels[1].Text != "CH2" ||
        Descendants(tabs.TabPages[4]).OfType<CheckBox>().Any(check => check.Text == "I0"))
        throw new InvalidOperationException("Serial channels must be created from the stream, not DAQ IO labels.");
    var serialChart = (Control)formType.GetField("serialWaveformChart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    var serialSplit = (SplitContainer)formType.GetField("serialChartLogSplit", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    if (serialSplit.Orientation != Orientation.Horizontal || serialSplit.Panel1.Height < 170 ||
        serialSplit.Panel2.Height < 110)
        throw new InvalidOperationException("Serial chart/log split is not usable.");
    var points = (System.Collections.ICollection)serialChart.GetType().GetProperty("Points")!.GetValue(serialChart)!;
    if (points.Count != 2)
        throw new InvalidOperationException("Serial numeric samples did not reach the waveform chart.");
    Console.WriteLine("Serial and simulation page probe passed.");
}
if (Environment.GetEnvironmentVariable("UI_PROBE_TRENDS") == "1" || probeFlags.Contains("trends"))
{
    var trendPage = tabs.TabPages.Cast<TabPage>().SingleOrDefault(page => page.Text == "趋势记录")
        ?? throw new InvalidOperationException("Trend page is missing.");
    var channelTree = (TreeView)(formType.GetField("trendChannelTree", BindingFlags.Instance | BindingFlags.NonPublic)?
        .GetValue(form) ?? throw new InvalidOperationException("Multi-channel selection tree is missing."));
    if ((TreeNativeStyles.GetExtendedStyle(channelTree.Handle) & 0x0004) == 0)
        throw new InvalidOperationException("Trend selector is missing native double buffering.");
    var laneViewport = (Panel)(formType.GetField("trendLaneViewport", BindingFlags.Instance | BindingFlags.NonPublic)?
        .GetValue(form) ?? throw new InvalidOperationException("Unit chart viewport is missing."));
    var zoomIn = (Button)(formType.GetField("trendZoomInButton", BindingFlags.Instance | BindingFlags.NonPublic)?
        .GetValue(form) ?? throw new InvalidOperationException("Trend zoom control is missing."));
    var zoomOut = (Button)(formType.GetField("trendZoomOutButton", BindingFlags.Instance | BindingFlags.NonPublic)?
        .GetValue(form) ?? throw new InvalidOperationException("Trend zoom control is missing."));
    var pauseButton = (Button)formType.GetField("trendPauseButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    var exportButton = (Button)formType.GetField("trendExportButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    TreeNode ChannelNode(string key) => channelTree.Nodes.Cast<TreeNode>()
        .SelectMany(root => root.Nodes.Cast<TreeNode>())
        .Single(node => (string)node.Tag!.GetType().GetProperty("Key")!.GetValue(node.Tag)! == key);
    var recorder = formType.GetField("trendRecorder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    var chart = formType.GetField("trendChart", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    var chartPoints = chart.GetType().GetProperty("Points")!;
    var trendPointType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.TrendPoint", true)!;
    var trendChannelType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.TrendChannel", true)!;
    var emptyChart = Activator.CreateInstance(chart.GetType(), nonPublic: true)!;
    using ((IDisposable)emptyChart)
    {
        ((Control)emptyChart).Size = new System.Drawing.Size(400, 240);
        var invalidPoints = Array.CreateInstance(trendPointType, 1);
        invalidPoints.SetValue(Activator.CreateInstance(trendPointType, DateTime.Now, double.NaN, false, 0L), 0);
        var testChannel = Activator.CreateInstance(trendChannelType, "AD:0", "AD7606", "CH1", "raw")!;
        chart.GetType().GetMethod("SetData")!.Invoke(emptyChart, new object[] { testChannel, invalidPoints, TimeSpan.FromMinutes(2) });
        chart.GetType().GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(emptyChart, new object[] { new MouseEventArgs(MouseButtons.None, 0, 100, 100, 0) });
        var unconfirmedPoints = Array.CreateInstance(trendPointType, 3);
        var now = DateTime.Now;
        unconfirmedPoints.SetValue(Activator.CreateInstance(trendPointType, now.AddSeconds(-10), 0.0, false, 0L), 0);
        unconfirmedPoints.SetValue(Activator.CreateInstance(trendPointType, now.AddSeconds(-5), 3.0, false, 0L), 1);
        unconfirmedPoints.SetValue(Activator.CreateInstance(trendPointType, now, 1.0, false, 0L), 2);
        chart.GetType().GetMethod("SetData")!.Invoke(emptyChart,
            new object[] { testChannel, unconfirmedPoints, TimeSpan.FromSeconds(10) });
        using var warningImage = new Bitmap(400, 240);
        ((Control)emptyChart).DrawToBitmap(warningImage, new System.Drawing.Rectangle(0, 0, 400, 240));
        var warningPixels = 0;
        for (var y = 45; y < 140; y++)
            for (var x = 75; x < 370; x++)
            {
                var color = warningImage.GetPixel(x, y);
                if (color.R > 140 && color.G is > 65 and < 160 && color.B < 110) warningPixels++;
            }
        if (warningPixels < 30)
            throw new InvalidOperationException("Unconfirmed AD raw values were not visibly plotted.");
        if (Environment.GetEnvironmentVariable("UI_PROBE_TREND_WARNING_CAPTURE") is { Length: > 0 } warningCapture)
            warningImage.Save(warningCapture);
        chart.GetType().GetMethod("SetData")!.Invoke(emptyChart,
            new object[] { testChannel, unconfirmedPoints, TimeSpan.FromMinutes(2) });
        using var earlyImage = new Bitmap(400, 240);
        ((Control)emptyChart).DrawToBitmap(earlyImage, new System.Drawing.Rectangle(0, 0, 400, 240));
        var earlyLinePixels = 0;
        for (var y = 45; y < 140; y++)
            for (var x = 75; x < 250; x++)
            {
                var color = earlyImage.GetPixel(x, y);
                if (color.R > 140 && color.G is > 65 and < 160 && color.B < 110) earlyLinePixels++;
            }
        if (earlyLinePixels < 20)
            throw new InvalidOperationException("The first seconds of a two-minute trend are compressed at the right edge.");
    }
    var snapshotType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.AcquisitionSnapshot", true)!;
    void PushSnapshot(int second)
    {
        var snapshot = Activator.CreateInstance(snapshotType, nonPublic: true)!;
        snapshotType.GetProperty("Timestamp")!.SetValue(snapshot, new DateTime(2026, 10, 17, 12, 0, second));
        snapshotType.GetProperty("Status")!.SetValue(snapshot, (ushort)8);
        snapshotType.GetProperty("Ad7606Raw")!.SetValue(snapshot, new short[] { (short)second, 0, 0, 0, 0, 0, 0, 0 });
        formType.GetMethod("UpdateSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new[] { snapshot });
    }
    PushSnapshot(1);
    tabs.SelectedTab = trendPage;
    Application.DoEvents();
    using (var treeMessages = new TreeItemMessageCounter(channelTree))
    {
        formType.GetMethod("RefreshTrendChart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        formType.GetMethod("RefreshTrendChart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        if (treeMessages.SetItemCalls != 0)
            throw new InvalidOperationException($"Unchanged trend values updated tree rows {treeMessages.SetItemCalls} times.");
    }
    var visibleBefore = ((System.Collections.ICollection)chartPoints.GetValue(chart)!).Count;
    pauseButton.PerformClick();
    ChannelNode("AD:0").Checked = true;
    Application.DoEvents();
    if (laneViewport.Controls.OfType<Panel>().Count(panel => panel.Visible) != 2)
        throw new InvalidOperationException("Different units must create separate visible chart panes.");
    var adChart = laneViewport.Controls.OfType<Panel>().SelectMany(Descendants).Single(control =>
        control.GetType().Name == "TrendChartControl" && !ReferenceEquals(control, chart));
    var otherChannel = ((System.Collections.IEnumerable)chartPoints.GetValue(adChart)!).Cast<object>().First();
    if ((double)otherChannel.GetType().GetProperty("Value")!.GetValue(otherChannel)! != 1)
        throw new InvalidOperationException("Paused view must allow inspecting another channel at the frozen time.");
    ChannelNode("AD:1").Checked = true;
    Application.DoEvents();
    if (laneViewport.Controls.OfType<Panel>().Count(panel => panel.Visible) != 2)
        throw new InvalidOperationException("Same-unit channels must overlay in one chart pane.");
    if (((Control)chart).Width < 300 || ((Control)chart).Height < 100 || adChart.Width < 300 || adChart.Height < 100)
        throw new InvalidOperationException($"Trend chart panes have invalid size: mA={((Control)chart).Size}, raw={adChart.Size}.");
    if (Environment.GetEnvironmentVariable("UI_PROBE_TREND_MULTIPANE_CAPTURE") is { Length: > 0 } multipaneCapture)
    {
        using var multipaneImage = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(multipaneImage, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        multipaneImage.Save(multipaneCapture);
        using var directChartImage = new Bitmap(((Control)chart).Width, ((Control)chart).Height);
        ((Control)chart).DrawToBitmap(directChartImage,
            new System.Drawing.Rectangle(0, 0, directChartImage.Width, directChartImage.Height));
        directChartImage.Save(Path.ChangeExtension(multipaneCapture, ".chart.png"));
    }
    var windowInput = (ComboBox)formType.GetField("trendWindowInput", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    zoomIn.PerformClick();
    if (windowInput.SelectedIndex != 0 || zoomIn.Enabled)
        throw new InvalidOperationException("Zoom-in did not reduce the time window.");
    zoomOut.PerformClick();
    if (windowInput.SelectedIndex != 1)
        throw new InvalidOperationException("Zoom-out did not restore the time window.");
    var adLane = laneViewport.Controls.OfType<Panel>().Single(panel => panel.Controls.Contains(adChart));
    var expand = adLane.Controls.OfType<Panel>().SelectMany(panel => panel.Controls.OfType<Button>()).Single();
    expand.PerformClick();
    Application.DoEvents();
    if (laneViewport.Controls.OfType<Panel>().Count(panel => panel.Visible) != 1)
        throw new InvalidOperationException("Expanding a pane must hide the other chart panes.");
    expand.PerformClick();
    ChannelNode("AD:0").Checked = false;
    ChannelNode("AD:1").Checked = false;
    Application.DoEvents();
    if (laneViewport.Controls.OfType<Panel>().Count(panel => panel.Visible) != 1)
        throw new InvalidOperationException("Unchecked channels must hide their chart pane.");
    var milliampGroup = channelTree.Nodes.Cast<TreeNode>().Single(node => (string)node.Tag! == "mA");
    milliampGroup.Checked = false;
    if (laneViewport.Controls.OfType<Panel>().Any(panel => panel.Visible))
        throw new InvalidOperationException("Unchecking a unit group must hide its chart pane.");
    milliampGroup.Checked = true;
    if (laneViewport.Controls.OfType<Panel>().Count(panel => panel.Visible) != 1)
        throw new InvalidOperationException("Rechecking a unit group must restore its chart pane.");
    PushSnapshot(2);
    var count = (long)recorder.GetType().GetProperty("SnapshotCount")!.GetValue(recorder)!;
    var visiblePaused = ((System.Collections.ICollection)chartPoints.GetValue(chart)!).Count;
    if (count != 2 || visibleBefore != 1 || visiblePaused != visibleBefore || !exportButton.Enabled)
        throw new InvalidOperationException("Pause must freeze the plot while recording continues and export becomes available.");
    pauseButton.PerformClick();
    var visibleResumed = ((System.Collections.ICollection)chartPoints.GetValue(chart)!).Count;
    if (visibleResumed != 2)
        throw new InvalidOperationException("Resume did not reveal samples captured while paused.");
    ChannelNode("AD:0").Checked = true;
    ChannelNode("AD:1").Checked = true;
    for (var second = 3; second <= 12; second++) PushSnapshot(second);
    Application.DoEvents();
    using (var renderedChart = new Bitmap(adChart.Width, adChart.Height))
    {
        adChart.DrawToBitmap(renderedChart, new System.Drawing.Rectangle(0, 0, adChart.Width, adChart.Height));
        var coloredPixels = 0;
        for (var y = 25; y < renderedChart.Height - 35; y++)
            for (var x = 62; x < renderedChart.Width - 68; x++)
            {
                var pixel = renderedChart.GetPixel(x, y);
                if (pixel.GetSaturation() > 0.45f && pixel.GetBrightness() < 0.82f) coloredPixels++;
            }
        if (coloredPixels < 30)
            throw new InvalidOperationException("Multi-channel pane displayed a grid but did not draw trend lines.");
    }
    var axisWindowField = formType.GetField("manualTrendAxisWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var axisEndField = formType.GetField("lastTrendAxisEnd", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var valueRangeProperty = adChart.GetType().GetProperty("ManualValueRange")!;
    var wheelMethod = adChart.GetType().GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var doubleClickMethod = adChart.GetType().GetMethod("OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var chartCenterX = adChart.Width / 2;
    var chartCenterY = adChart.Height / 2;
    wheelMethod.Invoke(adChart, new object[] { new MouseEventArgs(MouseButtons.None, 0, chartCenterX, chartCenterY, 120) });
    var zoomedTime = (TimeSpan?)axisWindowField.GetValue(form);
    var zoomedEnd = (DateTime)axisEndField.GetValue(form)!;
    if (zoomedTime is null || zoomedTime.Value >= TimeSpan.FromSeconds(12) || valueRangeProperty.GetValue(adChart) is not null)
        throw new InvalidOperationException("Wheel over the plot must zoom time without cropping values.");
    wheelMethod.Invoke(adChart, new object[] { new MouseEventArgs(MouseButtons.None, 0, adChart.Width - 12, chartCenterY, 120) });
    var zoomedValue = valueRangeProperty.GetValue(adChart);
    if (zoomedValue is null || (TimeSpan?)axisWindowField.GetValue(form) != zoomedTime)
        throw new InvalidOperationException("Wheel over the value axis must zoom only values.");
    PushSnapshot(13);
    Application.DoEvents();
    formType.GetMethod("RefreshTrendChart", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
    if ((TimeSpan?)axisWindowField.GetValue(form) != zoomedTime || valueRangeProperty.GetValue(adChart) is null)
        throw new InvalidOperationException("Live refresh discarded the mouse-wheel viewport.");
    if ((DateTime)axisEndField.GetValue(form)! <= zoomedEnd)
        throw new InvalidOperationException("Mouse-wheel zoom stopped the live time axis.");
    var axisWindowOnOtherPane = chart.GetType().GetField("sharedPlotWindow", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(chart);
    if (!Equals(axisWindowOnOtherPane, zoomedTime))
        throw new InvalidOperationException("Time zoom did not stay synchronized across unit panes.");
    if (Environment.GetEnvironmentVariable("UI_PROBE_TREND_ZOOM_CAPTURE") is { Length: > 0 } zoomCapture)
    {
        using var zoomImage = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(zoomImage, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        zoomImage.Save(zoomCapture);
    }
    wheelMethod.Invoke(adChart, new object[] { new MouseEventArgs(MouseButtons.None, 0, chartCenterX, adChart.Height - 12, 120) });
    if ((TimeSpan?)axisWindowField.GetValue(form) >= zoomedTime || !Equals(valueRangeProperty.GetValue(adChart), zoomedValue))
        throw new InvalidOperationException("Wheel over the time axis must zoom only time.");
    var xOnlyTime = (TimeSpan?)axisWindowField.GetValue(form);
    wheelMethod.Invoke(adChart, new object[] { new MouseEventArgs(MouseButtons.None, 0, adChart.Width - 12, chartCenterY, 120) });
    if ((TimeSpan?)axisWindowField.GetValue(form) != xOnlyTime || Equals(valueRangeProperty.GetValue(adChart), zoomedValue))
        throw new InvalidOperationException("Wheel over the value axis must zoom only values.");
    wheelMethod.Invoke(adChart, new object[] { new MouseEventArgs(MouseButtons.None, 0, chartCenterX, chartCenterY, -120) });
    if ((TimeSpan?)axisWindowField.GetValue(form) <= xOnlyTime)
        throw new InvalidOperationException("Reverse wheel must zoom the time axis back out.");
    doubleClickMethod.Invoke(adChart, new object[] { new MouseEventArgs(MouseButtons.Left, 2, chartCenterX, chartCenterY, 0) });
    if (axisWindowField.GetValue(form) is not null ||
        valueRangeProperty.GetValue(adChart) is not null)
        throw new InvalidOperationException("Double-click must restore the live auto-fit viewport.");
    var scaleChart = Activator.CreateInstance(chart.GetType(), nonPublic: true)!;
    using ((IDisposable)scaleChart)
    {
        ((Control)scaleChart).Size = new Size(400, 240);
        var smallPoints = Array.CreateInstance(trendPointType, 3);
        var largePoints = Array.CreateInstance(trendPointType, 3);
        var sampleEnd = DateTime.Now;
        for (var index = 0; index < 3; index++)
        {
            var timestamp = sampleEnd.AddSeconds(index - 2);
            smallPoints.SetValue(Activator.CreateInstance(trendPointType, timestamp, index == 1 ? 0.05 : 0.0, true, 0L), index);
            largePoints.SetValue(Activator.CreateInstance(trendPointType, timestamp, index == 1 ? 15.0 : 5.0, true, 0L), index);
        }
        var smallChannel = Activator.CreateInstance(trendChannelType, "SMALL", "test", "small", "V")!;
        var largeChannel = Activator.CreateInstance(trendChannelType, "LARGE", "test", "large", "V")!;
        var trendSeriesType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.TrendSeries", true)!;
        var testSeries = Array.CreateInstance(trendSeriesType, 2);
        testSeries.SetValue(Activator.CreateInstance(trendSeriesType, smallChannel, smallPoints, Color.Magenta), 0);
        testSeries.SetValue(Activator.CreateInstance(trendSeriesType, largeChannel, largePoints, Color.Blue), 1);
        chart.GetType().GetMethods().Single(method => method.Name == "SetSeries" && method.GetParameters().Length == 2)
            .Invoke(scaleChart, new object[] { testSeries, TimeSpan.FromSeconds(10) });
        var scaleWheel = scaleChart.GetType().GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var notch = 0; notch < 10; notch++)
            scaleWheel.Invoke(scaleChart, new object[] { new MouseEventArgs(MouseButtons.None, 0, 388, 170, 120) });
        var sharedRange = scaleChart.GetType().GetProperty("ManualValueRange")!.GetValue(scaleChart)!;
        var sharedSpan = (double)sharedRange.GetType().GetField("Item2")!.GetValue(sharedRange)! -
            (double)sharedRange.GetType().GetField("Item1")!.GetValue(sharedRange)!;
        if (sharedSpan > 0.2)
            throw new InvalidOperationException("Shared Y zoom cannot resolve a small spike beside a large series.");
        using var scaleImage = new Bitmap(400, 240);
        ((Control)scaleChart).DrawToBitmap(scaleImage, new Rectangle(0, 0, 400, 240));
        var visibleSmallSpike = 0;
        for (var y = 45; y < 110; y++)
            for (var x = 250; x < 325; x++)
            {
                var pixel = scaleImage.GetPixel(x, y);
                if (pixel.R > 190 && pixel.G < 100 && pixel.B > 130) visibleSmallSpike++;
            }
        if (visibleSmallSpike < 10)
            throw new InvalidOperationException("Shared Y zoom did not reveal the small channel spike.");
        if (Environment.GetEnvironmentVariable("UI_PROBE_TREND_SMALL_SPIKE_CAPTURE") is { Length: > 0 } spikeCapture)
            scaleImage.Save(spikeCapture);
        var mouseDown = scaleChart.GetType().GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var mouseMove = scaleChart.GetType().GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var mouseUp = scaleChart.GetType().GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
        mouseDown.Invoke(scaleChart, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 388, 170, 0) });
        mouseMove.Invoke(scaleChart, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 388, 145, 0) });
        mouseUp.Invoke(scaleChart, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 388, 145, 0) });
        var shiftedRange = scaleChart.GetType().GetProperty("ManualValueRange")!.GetValue(scaleChart)!;
        if (Equals(shiftedRange, sharedRange))
            throw new InvalidOperationException("Dragging the shared Y axis did not pan the visible value range.");
    }
    if (Environment.GetEnvironmentVariable("UI_PROBE_TREND_CAPTURE") is { Length: > 0 } trendCapture)
    {
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
        image.Save(trendCapture);
    }
    Console.WriteLine("Trend page, pause/resume and background recording passed.");
}
if (Environment.GetEnvironmentVariable("UI_PROBE_PROJECTS") == "1")
{
    var externalTimer = (System.Windows.Forms.Timer)formType.GetField("externalProjectTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    if (!externalTimer.Enabled || externalTimer.Interval > 3000)
        throw new InvalidOperationException("Automatic project monitoring was not started after opening the window.");
    tabs.SelectedIndex = 2;
    Application.DoEvents();
    var temporaryProject = Path.Combine(Path.GetTempPath(), "daq-ui-project-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporaryProject);
    File.WriteAllText(Path.Combine(temporaryProject, "io_logic_user.c"), "void IO_Logic_Main(void) {}\n");
    var catalog = formType.GetField("projectCatalog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    var catalogType = catalog.GetType();
    var project = catalogType.GetMethod("Register")!.Invoke(catalog, new object[] { temporaryProject })!;
    var projectId = project.GetType().GetProperty("Id")!.GetValue(project)!;
    try
    {
        var menuLabels = sourceTree.ContextMenuStrip!.Items.Cast<ToolStripItem>().Select(item => item.Text).ToArray();
        if (!menuLabels.Contains("从列表移除") || menuLabels.Contains("删除工程"))
            throw new InvalidOperationException("Project menu still uses destructive delete semantics.");
        var importedSourceNode = (TreeNode)formType.GetMethod("AddProjectTreeNode", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new[] { project })!;
        sourceTree.SelectedNode = importedSourceNode;
        Application.DoEvents();
        var entryPath = Path.Combine(temporaryProject, "io_logic_user.c");
        var initialSource = File.ReadAllText(entryPath);
        File.WriteAllText(entryPath, "external modification");
        var compiled = await (Task<bool>)formType.GetMethod("CompileCProgramAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, null)!;
        if (compiled || !compileOutput.Text.Contains("外部发生变化", StringComparison.Ordinal))
            throw new InvalidOperationException("Compile accepted an externally changed source file.");
        File.WriteAllText(entryPath, initialSource);
        var renamedEntryPath = Path.Combine(temporaryProject, "renamed.c");
        File.Move(entryPath, renamedEntryPath);
        var editorBeforeRename = codeEditor.Text;
        formType.GetMethod("CheckForExternalProjectChanges", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        if (codeEditor.Text != editorBeforeRename || !sourceTree.Nodes.Cast<TreeNode>()
                .Any(node => node.Tag?.GetType().Name == "ProjectReference" && node.Text.Contains("需处理")))
            throw new InvalidOperationException("External source rename did not update the tree without preserving the editor.");
        formType.GetMethod("SaveCProgram", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        if (File.Exists(entryPath)) throw new InvalidOperationException("Saving recreated an externally renamed source file.");
        var defaultProjectSource = sourceTree.Nodes.Cast<TreeNode>().Single(node => node.Text == "示例代码")
            .Nodes.Cast<TreeNode>().Single(node => node.Text == "io_logic_user.c");
        sourceTree.SelectedNode = defaultProjectSource;
        var unavailableRoot = sourceTree.Nodes.Cast<TreeNode>().Single(node => node.Tag?.GetType().Name == "ProjectReference"
            && (Guid)node.Tag.GetType().GetProperty("Id")!.GetValue(node.Tag)! == (Guid)projectId);
        sourceTree.SelectedNode = unavailableRoot;
        if (!codeEditor.ReadOnly) throw new InvalidOperationException("Opening an unavailable project did not show its missing state.");
        File.Move(renamedEntryPath, entryPath);
        formType.GetMethod("CheckForExternalProjectChanges", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        if (codeEditor.ReadOnly || codeEditor.Text != editorBeforeRename)
            throw new InvalidOperationException("Restored source was not reopened automatically.");
        var renamedProject = temporaryProject + "-renamed";
        Directory.Move(temporaryProject, renamedProject);
        temporaryProject = renamedProject;
        formType.GetMethod("SaveCProgram", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        if (Directory.Exists(renamedProject[..^"-renamed".Length]))
            throw new InvalidOperationException("Saving recreated the old project directory.");
        formType.GetMethod("AddUnavailableProjectTreeNode", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new[] { project });
        if (!sourceTree.Nodes.Cast<TreeNode>().Any(node => node.Text.Contains("需处理") && node.Nodes.Cast<TreeNode>().Any(child => child.Text.Contains("重新定位"))))
            throw new InvalidOperationException("Missing project is not manageable in the tree.");
        string reboundPath = string.Empty;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(6))
        {
            Application.DoEvents();
            reboundPath = formType.GetField("currentUserSourcePath", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)?.ToString();
            if (reboundPath == Path.Combine(renamedProject, "io_logic_user.c")) break;
            Thread.Sleep(50);
        }
        if (reboundPath != Path.Combine(renamedProject, "io_logic_user.c") || codeEditor.ReadOnly || codeEditor.Text != editorBeforeRename)
            throw new InvalidOperationException("The timer did not reconnect the externally renamed project.");
        var changePrompt = formType.GetField("externalSourceChangePrompt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var checkExternal = formType.GetMethod("CheckForExternalProjectChanges", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var renamedSource = Path.Combine(renamedProject, "io_logic_user.c");
        var promptCount = 0;
        changePrompt.SetValue(form, new Func<bool, DialogResult>(dirty =>
        {
            if (dirty) throw new InvalidOperationException("The clean source was unexpectedly marked dirty.");
            promptCount++;
            return DialogResult.No;
        }));
        File.WriteAllText(renamedSource, "external change one");
        checkExternal.Invoke(form, null);
        checkExternal.Invoke(form, null);
        if (promptCount != 1 || codeEditor.Text != editorBeforeRename)
            throw new InvalidOperationException("Declined external reload must preserve the editor without repeated prompts.");
        changePrompt.SetValue(form, new Func<bool, DialogResult>(_ => { promptCount++; return DialogResult.Yes; }));
        File.WriteAllText(renamedSource, "external change two");
        checkExternal.Invoke(form, null);
        if (promptCount != 2 || codeEditor.Text != "external change two")
            throw new InvalidOperationException("Confirmed external reload did not update the editor.");
        formType.GetMethod("SetEditorText", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { "unsaved editor change" });
        changePrompt.SetValue(form, new Func<bool, DialogResult>(dirty =>
        {
            if (!dirty) throw new InvalidOperationException("Unsaved editor content was not recognized.");
            promptCount++;
            return DialogResult.Cancel;
        }));
        File.WriteAllText(renamedSource, "external change three");
        checkExternal.Invoke(form, null);
        checkExternal.Invoke(form, null);
        if (promptCount != 3 || codeEditor.Text != "unsaved editor change")
            throw new InvalidOperationException("The editor buffer was discarded or the same conflict was prompted twice.");
        Console.WriteLine("Project UI probe passed: missing project remains manageable and save did not recreate files.");
    }
    finally
    {
        catalogType.GetMethod("Remove")!.Invoke(catalog, new[] { projectId });
        var target = Path.GetFullPath(temporaryProject);
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("daq-ui-project-", StringComparison.Ordinal))
            throw new InvalidOperationException("UI project probe cleanup escaped the temporary directory.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }
}
if (Environment.GetEnvironmentVariable("UI_PROBE_ATOMIC_HIGHLIGHT") == "1")
{
    tabs.SelectedIndex = 2;
    sourceTree.SelectedNode = sourceNode;
    Application.DoEvents();
    var setEditorText = formType.GetMethod("SetEditorText", BindingFlags.Instance | BindingFlags.NonPublic)!;
    setEditorText.Invoke(form, new object[] { string.Concat(Enumerable.Repeat("if (1) { IO_DO_Set(RELAY_1, 1); }\n", 80)) });
    void AssertColored(int line)
    {
        codeEditor.Select(codeEditor.GetFirstCharIndexFromLine(line), 2);
        if (codeEditor.SelectionColor == codeEditor.ForeColor)
        {
            throw new InvalidOperationException($"Line {line + 1} must be colored before the next UI repaint.");
        }
    }
    for (var line = 0; line < 5; line++) AssertColored(line);
    var initialLast = codeEditor.GetLineFromCharIndex(codeEditor.GetCharIndexFromPosition(
        new System.Drawing.Point(2, codeEditor.ClientSize.Height - 2)));
    AssertColored(initialLast - 1);
    codeEditor.Select(codeEditor.GetFirstCharIndexFromLine(60), 0);
    codeEditor.ScrollToCaret();
    var scrolledFirst = codeEditor.GetLineFromCharIndex(codeEditor.GetCharIndexFromPosition(new System.Drawing.Point(2, 2)));
    if (scrolledFirst < 10) throw new InvalidOperationException("Editor did not scroll to new code lines.");
    AssertColored(scrolledFirst + 1);
    AssertColored(scrolledFirst + 3);
    Console.WriteLine("Atomic visible highlighting passed after file load and scroll.");
}
if (Environment.GetEnvironmentVariable("UI_PROBE_PERF") == "1")
{
    tabs.SelectedIndex = 2;
    sourceTree.SelectedNode = sourceNode;
    Application.DoEvents();
    var longSource = string.Concat(Enumerable.Repeat(
        "if (IO_DI_Level(INPUT_1) == 1 || !IO_DI_Level(INPUT_2)) { IO_DO_Set(RELAY_1, 1); }\n", 800));
    var setEditorText = formType.GetMethod("SetEditorText", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var visibleHighlight = formType.GetMethod("HighlightVisibleCode", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    codeEditor.Text = longSource;
    stopwatch.Stop();
    Console.WriteLine($"Raw editor text assignment: {stopwatch.ElapsedMilliseconds} ms for 800 lines.");
    ((System.Windows.Forms.Timer)formType.GetField("editorDecorationTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!).Stop();
    stopwatch.Restart();
    visibleHighlight.Invoke(form, null);
    stopwatch.Stop();
    Console.WriteLine($"Visible range highlight: {stopwatch.ElapsedMilliseconds} ms for 800 lines.");
    stopwatch.Restart();
    setEditorText.Invoke(form, new object[] { longSource });
    stopwatch.Stop();
    Console.WriteLine($"Editor load and visible highlight: {stopwatch.ElapsedMilliseconds} ms for 800 lines.");
    if (stopwatch.ElapsedMilliseconds > 1000)
    {
        throw new InvalidOperationException("Opening code blocks the UI for more than 1000 ms.");
    }
    stopwatch.Restart();
    codeEditor.Select(codeEditor.GetFirstCharIndexFromLine(700), 0);
    stopwatch.Stop();
    Console.WriteLine($"Editor select line 700: {stopwatch.ElapsedMilliseconds} ms.");
    stopwatch.Restart();
    codeEditor.ScrollToCaret();
    stopwatch.Stop();
    Console.WriteLine($"Editor scroll and visible highlight: {stopwatch.ElapsedMilliseconds} ms near line 700.");
    if (stopwatch.ElapsedMilliseconds > 500)
    {
        throw new InvalidOperationException("Scrolling blocks the UI for more than 500 ms.");
    }
    var scrolledLine = codeEditor.GetLineFromCharIndex(codeEditor.GetCharIndexFromPosition(new System.Drawing.Point(2, 2)));
    codeEditor.Select(codeEditor.GetFirstCharIndexFromLine(scrolledLine + 2), 2);
    if (codeEditor.SelectionColor == codeEditor.ForeColor)
    {
        throw new InvalidOperationException("Long-file scrolling shows uncolored code.");
    }
    var highlight = formType.GetMethod("HighlightCSource", BindingFlags.Instance | BindingFlags.NonPublic)!;
    stopwatch.Restart();
    highlight.Invoke(form, new object[] { null, null });
    stopwatch.Stop();
    Console.WriteLine($"Editor highlight: {stopwatch.ElapsedMilliseconds} ms for 800 lines.");
    if (stopwatch.ElapsedMilliseconds > 250)
    {
        throw new InvalidOperationException("Editor highlighting blocks the UI for more than 250 ms.");
    }
    codeEditor.Select(0, 2);
    if (codeEditor.SelectionColor == codeEditor.ForeColor)
    {
        throw new InvalidOperationException("Visible C keyword lost syntax highlighting.");
    }
    var minimap = (Control)formType.GetField("codeMinimap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    stopwatch.Restart();
    minimap.Refresh();
    stopwatch.Stop();
    Console.WriteLine($"Minimap rebuild: {stopwatch.ElapsedMilliseconds} ms for 800 lines.");
    if (stopwatch.ElapsedMilliseconds > 300)
    {
        throw new InvalidOperationException("Minimap rebuild blocks the UI for more than 300 ms.");
    }
    stopwatch.Restart();
    minimap.Refresh();
    stopwatch.Stop();
    Console.WriteLine($"Cached minimap repaint: {stopwatch.ElapsedMilliseconds} ms.");
    stopwatch.Restart();
    codeEditor.AppendText("x");
    stopwatch.Stop();
    Console.WriteLine($"Editor text change: {stopwatch.ElapsedMilliseconds} ms for 800 lines.");
    if (stopwatch.ElapsedMilliseconds > 100)
    {
        throw new InvalidOperationException("Editor text change blocks the UI for more than 100 ms.");
    }
}
if (Environment.GetEnvironmentVariable("UI_PROBE_MONITOR_CAPTURE") is { Length: > 0 } monitorCapture)
{
    tabs.SelectedIndex = 0;
    Application.DoEvents();
    using var image = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
    image.Save(monitorCapture);
}
if (Environment.GetEnvironmentVariable("UI_PROBE_RS485_CAPTURE") is { Length: > 0 } rs485Capture)
{
    tabs.SelectedIndex = 1;
    Application.DoEvents();
    using var image = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
    image.Save(rs485Capture);
}
if (Environment.GetEnvironmentVariable("UI_PROBE_CAPTURE") is { Length: > 0 } capturePath)
{
    tabs.SelectedIndex = 2;
    Application.DoEvents();
    using var image = new Bitmap(form.Width, form.Height);
    form.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, form.Width, form.Height));
    image.Save(capturePath);
}
formType.GetField("currentLoadedSourceText", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, codeEditor.Text);
form.Close();

sealed class TreeItemMessageCounter : NativeWindow, IDisposable
{
    public int SetItemCalls { get; private set; }

    public TreeItemMessageCounter(TreeView tree) => AssignHandle(tree.Handle);

    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x110D or 0x113F) SetItemCalls++;
        base.WndProc(ref message);
    }

    public void Dispose() => ReleaseHandle();
}

static class TreeNativeStyles
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, IntPtr second);

    public static int GetExtendedStyle(IntPtr handle) =>
        (int)SendMessage(handle, 0x112D, IntPtr.Zero, IntPtr.Zero);

}
