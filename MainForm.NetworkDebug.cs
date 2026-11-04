using System.Text;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private readonly ComboBox networkModeInput = new(), networkSendEncoding = new();
    private readonly TextBox networkBindAddress = new(), networkBindPort = new(), networkHost = new(), networkTargetPort = new(), networkSendInput = new(), networkDebugLog = ToolLog();
    private readonly CheckBox networkDisplayHex = new() { Text = "HEX 显示", AutoSize = true, Margin = new Padding(6, 8, 0, 0) };
    private readonly Button networkStartButton = ToolButton("连接 / 启动", 110, true), networkSendButton = ToolButton("发送", 75, true);
    private readonly Label networkStatus = ToolLabel("未启动");
    private bool networkConnecting;

    private Control BuildNetworkDebugTool()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        var settings = ToolBar();
        networkModeInput.DropDownStyle = ComboBoxStyle.DropDownList; networkModeInput.Width = 105; networkModeInput.Items.AddRange(new object[] { "TCP 客户端", "TCP 服务端", "UDP" }); networkModeInput.SelectedIndex = 0;
        networkBindAddress.Text = "0.0.0.0"; networkBindAddress.Width = 105; networkBindPort.Text = "0"; networkBindPort.Width = 55;
        networkHost.Text = "127.0.0.1"; networkHost.Width = 105; networkTargetPort.Text = "5000"; networkTargetPort.Width = 55;
        settings.Controls.AddRange(new Control[] { ToolLabel("模式"), networkModeInput, ToolLabel("本机地址"), networkBindAddress, ToolLabel("端口"), networkBindPort, ToolLabel("目标地址"), networkHost, ToolLabel("端口"), networkTargetPort, networkStartButton });
        var info = ToolBar(); var clear = ToolButton("清空", 65); clear.Click += (_, _) => networkDebugLog.Clear();
        info.Controls.AddRange(new Control[] { networkStatus, networkDisplayHex, clear, ToolLabel("TCP 按数据块显示；服务端发送给所有已连接客户端。UDP 保留报文边界。") });
        var send = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        send.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); send.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        send.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); send.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        networkSendInput.Dock = DockStyle.Fill; networkSendInput.Text = "Hello";
        networkSendEncoding.DropDownStyle = ComboBoxStyle.DropDownList; networkSendEncoding.Width = 80; networkSendEncoding.Items.AddRange(new object[] { "UTF-8", "HEX" }); networkSendEncoding.SelectedIndex = 0;
        var actions = ToolBar(); actions.Controls.AddRange(new Control[] { networkSendEncoding, networkSendButton });
        send.Controls.Add(networkSendInput, 0, 0); send.Controls.Add(actions, 1, 0); send.Controls.Add(ToolLabel("HEX 输入例如 01 03 00 FF；UTF-8 按原文发送，不自动添加换行。"), 0, 1);
        root.Controls.Add(settings, 0, 0); root.Controls.Add(info, 0, 1); root.Controls.Add(networkDebugLog, 0, 2); root.Controls.Add(send, 0, 3);
        networkModeInput.SelectedIndexChanged += (_, _) => { if (!networkTool.IsRunning && !networkConnecting) { networkBindAddress.Text = networkModeInput.SelectedIndex == 1 ? "127.0.0.1" : "0.0.0.0"; networkBindPort.Text = networkModeInput.SelectedIndex == 1 ? "1500" : "0"; } };
        networkStartButton.Click += async (_, _) => await ToggleNetworkToolAsync();
        networkSendButton.Click += async (_, _) =>
        {
            try { networkSendButton.Enabled = false; var data = networkSendEncoding.SelectedIndex == 1 ? DataConversion.ParseHex(networkSendInput.Text) : Encoding.UTF8.GetBytes(networkSendInput.Text); await networkTool.SendAsync(data, networkHost.Text.Trim(), ParsePort(networkTargetPort.Text, false), CancellationToken.None); }
            catch (Exception ex) { AppendBounded(networkDebugLog, "发送失败（服务端可能已向部分客户端发送）：" + ex.Message); }
            finally { if (!IsDisposed) UpdateNetworkToolUi(); }
        };
        UpdateNetworkToolUi(); return root;
    }
    private async Task ToggleNetworkToolAsync()
    {
        if (networkConnecting) { networkConnectCancellation?.Cancel(); return; }
        if (networkTool.IsRunning) { networkTool.Stop(); AppendBounded(networkDebugLog, "已停止。"); UpdateNetworkToolUi(); return; }
        try
        {
            var address = ParseBindAddress(networkBindAddress.Text); var localPort = ParsePort(networkBindPort.Text, true); var targetPort = ParsePort(networkTargetPort.Text, false);
            networkConnecting = true; networkConnectCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)); UpdateNetworkToolUi();
            await networkTool.StartAsync((NetworkMode)networkModeInput.SelectedIndex, address, localPort, networkHost.Text.Trim(), targetPort, networkConnectCancellation.Token);
        }
        catch (OperationCanceledException) { AppendBounded(networkDebugLog, "连接已取消或超时。"); }
        catch (Exception ex) { AppendBounded(networkDebugLog, "启动失败：" + ex.Message); }
        finally { networkConnecting = false; networkConnectCancellation?.Dispose(); networkConnectCancellation = null; if (!IsDisposed) UpdateNetworkToolUi(); }
    }
    private void UpdateNetworkToolUi()
    {
        var running = networkTool.IsRunning; networkStartButton.Text = networkConnecting ? "取消连接" : running ? "停止" : "连接 / 启动";
        networkModeInput.Enabled = networkBindAddress.Enabled = networkBindPort.Enabled = !running && !networkConnecting;
        networkHost.Enabled = networkTargetPort.Enabled = !networkConnecting; networkSendButton.Enabled = running;
        networkStatus.Text = networkConnecting ? "连接中…" : running ? $"已启动 {networkTool.LocalEndpoint} · TCP 对端 {networkTool.PeerCount}" : "未启动";
    }
}

