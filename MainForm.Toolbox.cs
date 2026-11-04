using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private readonly TabControl toolboxTabs = new();
    private readonly NetworkToolService networkTool = new();
    private readonly ConcurrentQueue<NetworkEvent> toolNetworkEvents = new();
    private int toolNetworkQueueCount, toolNetworkDropped;
    private readonly System.Windows.Forms.Timer toolboxTimer = new() { Interval = 100 };
    private CancellationTokenSource? networkConnectCancellation;
    private CancellationTokenSource? networkDiagnosticCancellation;
    private ModbusTcpSimulator? toolModbusServer;
    private List<ModbusTcpUnit> toolModbusUnits = new();
    private readonly ConcurrentQueue<string> toolModbusEvents = new();
    private int toolModbusQueueCount, toolModbusDropped;

    private Control BuildNetworkToolbox()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = UiPalette.Surface };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(SectionTitle("通信与数据工具箱"), 0, 0);
        toolboxTabs.Dock = DockStyle.Fill;
        toolboxTabs.Font = new Font("Microsoft YaHei UI", 9.5F);
        toolboxTabs.Appearance = TabAppearance.FlatButtons;
        toolboxTabs.SizeMode = TabSizeMode.Fixed;
        toolboxTabs.ItemSize = new Size(190, 60);
        toolboxTabs.Multiline = true;
        toolboxTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        toolboxTabs.DrawItem += DrawToolboxTab;
        toolboxTabs.SelectedIndexChanged += (_, _) => toolboxTabs.Invalidate();
        void Add(string title, Control content)
        {
            var tab = new TabPage(title) { BackColor = UiPalette.Surface, Padding = new Padding(10) };
            tab.Controls.Add(content); toolboxTabs.TabPages.Add(tab);
        }
        Add("TCP/UDP 调试", BuildNetworkDebugTool());
        Add("数据格式转换", BuildDataConversionTool());
        Add("Modbus TCP 虚拟从站", BuildModbusTcpTool());
        Add("网络诊断", BuildNetworkDiagnosticsTool());
        root.Controls.Add(toolboxTabs, 0, 1);
        networkTool.Event += evt =>
        {
            if (Interlocked.Increment(ref toolNetworkQueueCount) <= 2048) toolNetworkEvents.Enqueue(evt);
            else { Interlocked.Decrement(ref toolNetworkQueueCount); Interlocked.Increment(ref toolNetworkDropped); }
        };
        toolboxTimer.Tick += (_, _) => FlushToolboxEvents();
        toolboxTimer.Start();
        return root;
    }
    private static void DrawToolboxTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabCount) return;
        var selected = e.Index == tabs.SelectedIndex;
        var bounds = e.Bounds;
        bounds.Inflate(-5, -4);
        if (bounds.Width < 20 || bounds.Height < 20) return;
        using var fill = new SolidBrush(selected ? UiPalette.BlueSoft : UiPalette.CodeReadOnlyBackground);
        using var border = new Pen(selected ? UiPalette.Blue : UiPalette.Border);
        e.Graphics.FillRectangle(fill, bounds);
        e.Graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        if (selected)
        {
            using var accent = new SolidBrush(UiPalette.Blue);
            e.Graphics.FillRectangle(accent, bounds.X, bounds.Y + 1, 3, bounds.Height - 2);
        }
        var titles = new[] { "TCP/UDP 调试", "数据格式转换", "Modbus TCP 虚拟从站", "网络诊断" };
        var details = new[] { "网络收发与监听", "数值与字节序", "模拟多个从站", "网卡与连通性" };
        var title = new Rectangle(bounds.X + 12, bounds.Y + 5, bounds.Width - 20, 24);
        var detail = new Rectangle(bounds.X + 12, bounds.Y + 29, bounds.Width - 20, 18);
        TextRenderer.DrawText(e.Graphics, titles[e.Index], tabs.Font, title, selected ? UiPalette.Blue : UiPalette.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        using var subtitleFont = new Font(tabs.Font.FontFamily, 8F);
        TextRenderer.DrawText(e.Graphics, details[e.Index], subtitleFont, detail, UiPalette.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (tabs.Focused && selected && (e.State & DrawItemState.Focus) != 0)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -3, -3));
    }
    private static Label ToolLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(6, 9, 4, 0), ForeColor = UiPalette.Muted };
    private static Button ToolButton(string text, int width, bool primary = false)
    {
        var button = new Button { Text = text, Width = width, Height = 30, Margin = new Padding(3, 3, 4, 0) };
        UiPalette.StyleButton(button, primary); return button;
    }
    private static FlowLayoutPanel ToolBar() => new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, BackColor = UiPalette.Surface };
    private static TextBox ToolLog() => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, BorderStyle = BorderStyle.FixedSingle, BackColor = UiPalette.CodeBackground, ForeColor = UiPalette.CodeText, Font = new Font("Consolas", 9F) };
    private static void AppendBounded(TextBox box, string text)
    {
        if (box.TextLength > 200000) { box.Select(0, Math.Min(50000, box.TextLength)); box.SelectedText = ""; }
        box.AppendText(text + Environment.NewLine);
    }
    private static IPAddress ParseBindAddress(string text)
    {
        if (!IPAddress.TryParse(text.Trim(), out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException("本机绑定地址必须是有效的 IPv4 地址；0.0.0.0 表示全部 IPv4 网卡。");
        return address;
    }
    private static int ParsePort(string text, bool allowZero)
    {
        if (!int.TryParse(text.Trim(), out var port) || port < (allowZero ? 0 : 1) || port > 65535)
            throw new ArgumentException(allowZero ? "本机端口范围为 0–65535，0 表示系统自动分配。" : "目标端口范围为 1–65535。");
        return port;
    }
    private void FlushToolboxEvents()
    {
        if (networkTool.IsRunning && !networkConnecting) UpdateNetworkToolUi();
        var dropped = Interlocked.Exchange(ref toolNetworkDropped, 0);
        if (dropped > 0) AppendBounded(networkDebugLog, $"[日志] 数据量过大，省略 {dropped} 个显示块。");
        for (var i = 0; i < 100 && toolNetworkEvents.TryDequeue(out var evt); i++)
        {
            Interlocked.Decrement(ref toolNetworkQueueCount);
            var data = evt.Data.Length == 0 ? evt.Message : networkDisplayHex.Checked
                ? Convert.ToHexString(evt.Data) : Encoding.UTF8.GetString(evt.Data);
            if (data.Length > 16000) data = data[..16000] + "…（此显示块已截断）";
            AppendBounded(networkDebugLog, $"{DateTime.Now:HH:mm:ss.fff} {evt.Kind} {evt.Peer}  {data}");
            if (evt.Kind == "CLOSED") UpdateNetworkToolUi();
        }
        dropped = Interlocked.Exchange(ref toolModbusDropped, 0);
        if (dropped > 0) AppendBounded(toolModbusLog, $"[日志] 数据量过大，省略 {dropped} 条。");
        for (var i = 0; i < 100 && toolModbusEvents.TryDequeue(out var message); i++)
        { Interlocked.Decrement(ref toolModbusQueueCount); AppendBounded(toolModbusLog, $"{DateTime.Now:HH:mm:ss.fff}  {message}"); }
    }
    private void EnqueueModbusLog(string message)
    {
        if (Interlocked.Increment(ref toolModbusQueueCount) <= 2048) toolModbusEvents.Enqueue(message);
        else { Interlocked.Decrement(ref toolModbusQueueCount); Interlocked.Increment(ref toolModbusDropped); }
    }
    private void StopNetworkTools()
    {
        networkConnectCancellation?.Cancel(); networkDiagnosticCancellation?.Cancel();
        networkTool.Dispose(); toolModbusServer?.Dispose();
        toolboxTimer.Stop(); toolboxTimer.Dispose();
    }
}


