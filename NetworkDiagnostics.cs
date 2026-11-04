using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace IndustrialFieldDataAcquisitionTerminal;

internal static class NetworkDiagnostics
{
    public static string LocalAdapters()
    {
        var text = new StringBuilder();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().OrderBy(a => a.Name))
        {
            text.AppendLine($"{adapter.Name} · {adapter.NetworkInterfaceType} · {adapter.OperationalStatus}");
            try
            {
                var properties = adapter.GetIPProperties();
                foreach (var address in properties.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                    text.AppendLine($"  IPv4 {address.Address} / {address.IPv4Mask}");
                foreach (var gateway in properties.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                    text.AppendLine($"  网关 {gateway.Address}");
            }
            catch (NetworkInformationException ex) { text.AppendLine("  无法读取：" + ex.Message); }
        }
        return text.Length == 0 ? "没有发现网络适配器。" : text.ToString();
    }
    public static async Task<string> CheckAsync(string host, int port, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) throw new ArgumentException("请输入目标地址和 TCP 端口。");
        var text = new StringBuilder();
        var addresses = await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
        text.AppendLine("DNS / IP：" + string.Join(", ", addresses.Select(a => a.ToString())));
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (address is null) { text.AppendLine("没有 IPv4 地址，无法进行 IPv4 连接测试。"); return text.ToString(); }
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, 2000).WaitAsync(token).ConfigureAwait(false);
            text.AppendLine(reply.Status == IPStatus.Success ? $"Ping：成功，{reply.RoundtripTime} ms" : $"Ping：{reply.Status}（不代表 TCP 一定不可用）");
        }
        catch (Exception ex) when (ex is PingException or SocketException) { text.AppendLine("Ping 失败：" + ex.Message); }
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await client.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
            text.AppendLine($"TCP {address}:{port}：连接成功，{watch.ElapsedMilliseconds} ms（仅验证连接，不发送业务请求）");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { text.AppendLine($"TCP {address}:{port}：连接超时"); }
        catch (SocketException ex) { text.AppendLine($"TCP {address}:{port}：{ex.Message}（代码 {ex.SocketErrorCode}）"); }
        return text.ToString();
    }
}
