namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private Control BuildNetworkDiagnosticsTool()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var host = new TextBox { Width = 150, Text = "192.168.1.10" }; var port = new TextBox { Width = 65, Text = "5000" };
        var adapters = ToolButton("查看本机网卡", 110); var check = ToolButton("检查目标", 95, true); var cancel = ToolButton("取消", 65); cancel.Enabled = false;
        var output = ToolLog(); var bar = ToolBar(); bar.Controls.AddRange(new Control[] { ToolLabel("目标地址"), host, ToolLabel("TCP 端口"), port, adapters, check, cancel });
        var help = ToolBar(); help.Controls.Add(ToolLabel("检查 DNS、Ping 和 TCP 连接；Ping 被禁用时，TCP 仍可能正常。"));
        adapters.Click += async (_, _) => { try { adapters.Enabled = false; var result = await Task.Run(NetworkDiagnostics.LocalAdapters); if (!IsDisposed) AppendBounded(output, result); } catch (Exception ex) { if (!IsDisposed) AppendBounded(output, "读取失败：" + ex.Message); } finally { if (!IsDisposed) adapters.Enabled = true; } };
        cancel.Click += (_, _) => networkDiagnosticCancellation?.Cancel();
        check.Click += async (_, _) =>
        {
            try { var targetPort = ParsePort(port.Text, false); var target = host.Text.Trim(); networkDiagnosticCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10)); check.Enabled = false; cancel.Enabled = true; AppendBounded(output, $"检查 {target}:{targetPort} …"); var result = await NetworkDiagnostics.CheckAsync(target, targetPort, networkDiagnosticCancellation.Token); if (!IsDisposed) AppendBounded(output, result); }
            catch (OperationCanceledException) { if (!IsDisposed) AppendBounded(output, "已取消或检查超时。"); }
            catch (Exception ex) { if (!IsDisposed) AppendBounded(output, "检查失败：" + ex.Message); }
            finally { networkDiagnosticCancellation?.Dispose(); networkDiagnosticCancellation = null; if (!IsDisposed) { check.Enabled = true; cancel.Enabled = false; } }
        };
        root.Controls.Add(bar, 0, 0); root.Controls.Add(help, 0, 1); root.Controls.Add(output, 0, 2); return root;
    }
}
