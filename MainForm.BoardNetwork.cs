using System.Net;
using System.Net.Sockets;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private async void OnBoardNetworkClicked(object? sender, EventArgs e)
    {
        if (!client.IsOpen) { MessageBox.Show(this, "请先连接板卡，再读取和修改板卡网络参数。", "板卡网络配置"); return; }
        byte[] response;
        try
        {
            await pollGate.WaitAsync();
            try { response = await Task.Run(() => client.TransactCustom(0x42, 1, Array.Empty<byte>(), 18)); }
            finally { pollGate.Release(); }
            if (response[3] != 0) throw new InvalidOperationException($"板卡返回错误 {response[3]}。");
        }
        catch (Exception ex) { MessageBox.Show(this, $"读取失败：{ex.Message}\n请确认板卡已烧录支持网络配置的新固件。", "板卡网络配置"); return; }

        using var dialog = new Form { Text = "板卡网络配置", Width = 430, Height = 315, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 6 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        TextBox Field(string title, string value, int row)
        {
            table.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var box = new TextBox { Text = value, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 5) };
            table.Controls.Add(box, 1, row); return box;
        }
        var ip = Field("板卡 IP", new IPAddress(response.AsSpan(4, 4)).ToString(), 0);
        var mask = Field("子网掩码", new IPAddress(response.AsSpan(8, 4)).ToString(), 1);
        var gateway = Field("网关", new IPAddress(response.AsSpan(12, 4)).ToString(), 2);
        var port = Field("TCP 端口", ((response[16] << 8) | response[17]).ToString(), 3);
        var tip = new Label { Text = "保存后板卡自动复位，请确保电脑可访问新网段。", AutoSize = true, ForeColor = Color.DimGray };
        table.Controls.Add(tip, 0, 4); table.SetColumnSpan(tip, 2);
        var save = new Button { Text = "保存并复位", AutoSize = true, Dock = DockStyle.Right };
        table.Controls.Add(save, 1, 5); dialog.Controls.Add(table);
        save.Click += async (_, _) =>
        {
            try
            {
                static byte[] Parse(string text) => IPAddress.TryParse(text.Trim(), out var a) && a.AddressFamily == AddressFamily.InterNetwork
                    ? a.GetAddressBytes() : throw new ArgumentException("请输入有效的 IPv4 地址。");
                var ipBytes = Parse(ip.Text); var maskBytes = Parse(mask.Text); var gatewayBytes = Parse(gateway.Text);
                if (!ushort.TryParse(port.Text, out var newPort) || newPort == 0) throw new ArgumentException("TCP 端口应为 1–65535。");
                static uint Number(byte[] a) => ((uint)a[0] << 24) | ((uint)a[1] << 16) | ((uint)a[2] << 8) | a[3];
                var ipNumber = Number(ipBytes); var maskNumber = Number(maskBytes); var gwNumber = Number(gatewayBytes);
                var hostBits = ipNumber & ~maskNumber;
                if (ipBytes[0] is 0 or 127 or >= 224 || maskNumber is 0 or uint.MaxValue ||
                    ((~maskNumber & (~maskNumber + 1)) != 0) || hostBits == 0 || hostBits == ~maskNumber)
                    throw new ArgumentException("板卡 IP 或子网掩码无效，请填写有效的主机地址和连续子网掩码。");
                if (gwNumber != 0 && ((gwNumber & maskNumber) != (ipNumber & maskNumber) ||
                    (gwNumber & ~maskNumber) == 0 || (gwNumber & ~maskNumber) == ~maskNumber || gwNumber == ipNumber))
                    throw new ArgumentException("网关必须为空（0.0.0.0）或位于板卡所在子网内的有效主机地址。");
                var payload = new byte[14]; ipBytes.CopyTo(payload, 0); maskBytes.CopyTo(payload, 4); gatewayBytes.CopyTo(payload, 8);
                payload[12] = (byte)(newPort >> 8); payload[13] = (byte)newPort;
                save.Enabled = false;
                await pollGate.WaitAsync();
                try
                {
                    var result = await Task.Run(() => client.TransactCustom(0x42, 2, payload, 4, 10000));
                    if (result[3] != 0) throw new InvalidOperationException("板卡拒绝保存，请检查 IP、掩码、网关和端口。");
                }
                finally { pollGate.Release(); }
                var newHost = new IPAddress(ipBytes).ToString(); var unit = (byte)slaveAddressInput.Value;
                dialog.Close(); Disconnect(); SetStatus($"板卡已保存并复位，正在连接 {newHost}:{newPort}...");
                await Task.Delay(1500);
                Exception? last = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await openConnection(newHost, newPort, unit, timeout.Token);
                        var verify = await Task.Run(() => client.TransactCustom(0x42, 1, Array.Empty<byte>(), 18));
                        if (verify[3] != 0 || !verify.AsSpan(4, 14).SequenceEqual(payload)) throw new InvalidOperationException("新地址读回配置不一致。");
                        hostInput.Text = newHost; ethernetPortInput.Value = newPort;
                        Interlocked.Increment(ref connectionEpoch); PrepareTrendForDevice(newHost, newPort, unit); SetConnectedUi(true);
                        if (deviceSnapshot is not null && selectedDeviceId is not null)
                            SaveDevices(deviceSnapshot.Devices.Select(p => p.Id == selectedDeviceId ? p with { Host = newHost, Port = newPort } : p).ToArray());
                        SetStatus($"板卡网络配置已生效：{newHost}:{newPort}"); return;
                    }
                    catch (Exception ex) { last = ex; client.Close(); await Task.Delay(1000); }
                }
                SetConnectedUi(false);
                MessageBox.Show(this, $"板卡已确认保存，但无法连接新地址 {newHost}:{newPort}。请检查网线、IP 冲突和电脑网段，再手动连接。\n{last?.Message}", "板卡网络配置");
            }
            catch (Exception ex)
            {
                if (!dialog.IsDisposed) { save.Enabled = true; MessageBox.Show(dialog, $"配置未确认：{ex.Message}\n如果连接中断，请分别尝试新旧地址。", "板卡网络配置"); }
                else MessageBox.Show(this, ex.Message, "板卡网络配置");
            }
        };
        dialog.ShowDialog(this);
    }
}
