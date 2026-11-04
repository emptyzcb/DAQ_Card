using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace IndustrialFieldDataAcquisitionTerminal;

internal enum NetworkMode { TcpClient, TcpServer, Udp }
internal sealed record NetworkEvent(string Kind, string Peer, byte[] Data, string Message);
internal sealed class NetworkToolService : IDisposable
{
    private sealed record Peer(TcpClient Client, SemaphoreSlim SendLock);
    private readonly ConcurrentDictionary<string, Peer> peers = new();
    private CancellationTokenSource? cancellation;
    private TcpListener? listener;
    private UdpClient? udp;
    private long generation;
    private NetworkMode mode;
    public bool IsRunning => cancellation is not null;
    public int PeerCount => peers.Count;
    public IPEndPoint? LocalEndpoint => listener?.LocalEndpoint as IPEndPoint ?? udp?.Client.LocalEndPoint as IPEndPoint ??
        peers.Values.Select(p => p.Client.Client.LocalEndPoint as IPEndPoint).FirstOrDefault(p => p is not null);
    public event Action<NetworkEvent>? Event;
    private void Emit(long epoch, string kind, string peer, byte[]? data = null, string message = "")
    { if (epoch == Interlocked.Read(ref generation)) Event?.Invoke(new(kind, peer, data ?? Array.Empty<byte>(), message)); }
    public async Task StartAsync(NetworkMode selectedMode, IPAddress bindAddress, int bindPort, string host, int targetPort, CancellationToken token)
    {
        if (IsRunning) throw new InvalidOperationException("请先停止当前网络工具连接。");
        if (bindAddress.AddressFamily != AddressFamily.InterNetwork || bindPort is < 0 or > 65535 || targetPort is < 1 or > 65535)
            throw new ArgumentException("请输入有效的 IPv4 地址和端口。");
        var epoch = Interlocked.Increment(ref generation);
        var own = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, own.Token);
        try
        {
            mode = selectedMode;
            if (selectedMode == NetworkMode.TcpClient)
            {
                var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
                try
                {
                    client.Client.Bind(new IPEndPoint(bindAddress, bindPort));
                    await client.ConnectAsync(host, targetPort, linked.Token).ConfigureAwait(false);
                    linked.Token.ThrowIfCancellationRequested();
                    var key = client.Client.RemoteEndPoint!.ToString()!;
                    peers[key] = new Peer(client, new SemaphoreSlim(1, 1));
                    cancellation = own;
                    Emit(epoch, "INFO", key, message: "TCP 已连接；接收按数据块显示，TCP 本身没有报文边界。");
                    _ = ReadPeerAsync(epoch, key, peers[key], own.Token);
                }
                catch { client.Dispose(); throw; }
            }
            else if (selectedMode == NetworkMode.TcpServer)
            {
                var server = new TcpListener(bindAddress, bindPort);
                server.Start(16); listener = server; cancellation = own;
                Emit(epoch, "INFO", "", message: $"TCP 正在监听 {server.LocalEndpoint}");
                _ = AcceptAsync(epoch, server, own.Token);
            }
            else
            {
                var socket = new UdpClient(new IPEndPoint(bindAddress, bindPort));
                udp = socket; cancellation = own;
                Emit(epoch, "INFO", "", message: $"UDP 已绑定 {socket.Client.LocalEndPoint}");
                _ = ReadUdpAsync(epoch, socket, own.Token);
            }
        }
        catch { own.Dispose(); throw; }
    }
    private async Task AcceptAsync(long epoch, TcpListener server, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await server.AcceptTcpClientAsync(token).ConfigureAwait(false);
                var key = client.Client.RemoteEndPoint!.ToString()!;
                if (peers.Count >= 16 || !peers.TryAdd(key, new Peer(client, new SemaphoreSlim(1, 1))))
                { client.Dispose(); Emit(epoch, "ERROR", key, message: "连接数已达上限或地址重复，已拒绝。"); continue; }
                Emit(epoch, "INFO", key, message: "客户端已连接");
                _ = ReadPeerAsync(epoch, key, peers[key], token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        { if (!token.IsCancellationRequested) Emit(epoch, "ERROR", "", message: "监听停止：" + ex.Message); }
    }
    private async Task ReadPeerAsync(long epoch, string key, Peer peer, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            var stream = peer.Client.GetStream();
            while (!token.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (count == 0) break;
                Emit(epoch, "RX", key, buffer.AsSpan(0, count).ToArray());
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException or InvalidOperationException)
        { if (!token.IsCancellationRequested) Emit(epoch, "ERROR", key, message: ex.Message); }
        finally
        {
            if (peers.TryGetValue(key, out var existing) && ReferenceEquals(existing, peer)) peers.TryRemove(key, out _);
            peer.Client.Dispose();
            Emit(epoch, "INFO", key, message: "TCP 已断开");
            if (mode == NetworkMode.TcpClient && epoch == Interlocked.Read(ref generation))
            { cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null; Emit(epoch, "CLOSED", key, message: "客户端连接已关闭"); }
        }
    }
    private async Task ReadUdpAsync(long epoch, UdpClient socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var data = await socket.ReceiveAsync(token).ConfigureAwait(false);
                Emit(epoch, "RX", data.RemoteEndPoint.ToString(), data.Buffer);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        { if (!token.IsCancellationRequested) Emit(epoch, "ERROR", "", message: "UDP 接收失败：" + ex.Message); }
    }
    public async Task SendAsync(byte[] data, string host, int port, CancellationToken token)
    {
        if (!IsRunning) throw new InvalidOperationException("请先连接或启动监听。");
        if (data.Length > 65507) throw new ArgumentException("单次发送不能超过 65507 字节。");
        var epoch = Interlocked.Read(ref generation);
        if (mode == NetworkMode.Udp)
        {
            if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) throw new ArgumentException("请输入 UDP 目标地址和端口。");
            var addresses = await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? throw new ArgumentException("目标没有 IPv4 地址。");
            var endpoint = new IPEndPoint(address, port);
            await udp!.SendAsync(data, endpoint, token).ConfigureAwait(false);
            Emit(epoch, "TX", endpoint.ToString(), data);
        }
        else
        {
            var current = peers.ToArray();
            if (current.Length == 0) throw new InvalidOperationException("没有已连接的 TCP 对端。");
            foreach (var (key, peer) in current)
            {
                await peer.SendLock.WaitAsync(token).ConfigureAwait(false);
                try { await peer.Client.GetStream().WriteAsync(data, token).ConfigureAwait(false); Emit(epoch, "TX", key, data); }
                finally { peer.SendLock.Release(); }
            }
        }
    }
    public void Stop()
    {
        Interlocked.Increment(ref generation);
        var old = cancellation; cancellation = null;
        old?.Cancel(); listener?.Stop(); listener = null; udp?.Dispose(); udp = null;
        foreach (var peer in peers.Values) peer.Client.Dispose();
        peers.Clear(); old?.Dispose();
    }
    public void Dispose() => Stop();
}

