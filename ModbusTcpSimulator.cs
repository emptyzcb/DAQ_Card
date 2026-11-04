using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class ModbusTcpUnit
{
    private readonly object gate = new();
    public byte UnitId { get; }
    public ushort Start { get; }
    private readonly ushort[] holding, input;
    private readonly bool[] coils, discrete;
    public ModbusTcpUnit(byte unit, ushort start, ushort[] holding, ushort[] input, bool[] coils, bool[] discrete)
    {
        if (unit is 0 or > 247 || holding.Length > 2048 || input.Length > 2048 || coils.Length > 2048 || discrete.Length > 2048 ||
            new[] { holding.Length, input.Length, coils.Length, discrete.Length }.Any(n => (int)start + n > 65536))
            throw new ArgumentException("单元 ID、起始地址或数据数量超出范围。");
        UnitId = unit; Start = start; this.holding = (ushort[])holding.Clone(); this.input = (ushort[])input.Clone();
        this.coils = (bool[])coils.Clone(); this.discrete = (bool[])discrete.Clone();
    }
    public (ushort[] Holding, ushort[] Input, bool[] Coils, bool[] Discrete) Snapshot()
    { lock (gate) return ((ushort[])holding.Clone(), (ushort[])input.Clone(), (bool[])coils.Clone(), (bool[])discrete.Clone()); }
    public byte[] Process(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length == 0) throw new InvalidDataException("空 Modbus PDU。");
        var fn = pdu[0];
        byte[] Error(byte code) => new byte[] { (byte)(fn | 0x80), code };
        if (fn is not (1 or 2 or 3 or 4 or 5 or 6 or 15 or 16)) return Error(1);
        if (pdu.Length < 5) return Error(3);
        var address = BinaryPrimitives.ReadUInt16BigEndian(pdu[1..]);
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu[3..]);
        var offset = (int)address - Start;
        lock (gate)
        {
            if (fn is >= 1 and <= 4)
            {
                var max = fn <= 2 ? 2000 : 125;
                var length = fn switch { 1 => coils.Length, 2 => discrete.Length, 3 => holding.Length, _ => input.Length };
                if (pdu.Length != 5 || quantity < 1 || quantity > max) return Error(3);
                if (offset < 0 || offset + quantity > length) return Error(2);
                if (fn <= 2)
                {
                    var source = fn == 1 ? coils : discrete;
                    var response = new byte[2 + (quantity + 7) / 8]; response[0] = fn; response[1] = (byte)(response.Length - 2);
                    for (var i = 0; i < quantity; i++) if (source[offset + i]) response[2 + i / 8] |= (byte)(1 << (i % 8));
                    return response;
                }
                var words = fn == 3 ? holding : input;
                var data = new byte[2 + quantity * 2]; data[0] = fn; data[1] = (byte)(quantity * 2);
                for (var i = 0; i < quantity; i++) BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2 + 2 * i), words[offset + i]);
                return data;
            }
            if (fn == 5 || fn == 6)
            {
                if (pdu.Length != 5) return Error(3);
                if (offset < 0 || offset >= (fn == 5 ? coils.Length : holding.Length)) return Error(2);
                if (fn == 5 && quantity is not (0xFF00 or 0)) return Error(3);
                if (fn == 5) coils[offset] = quantity == 0xFF00; else holding[offset] = quantity;
                return pdu.ToArray();
            }
            var maximum = fn == 15 ? 1968 : 123;
            if (quantity < 1 || quantity > maximum || pdu.Length < 6) return Error(3);
            var byteCount = pdu[5]; var expected = fn == 15 ? (quantity + 7) / 8 : quantity * 2;
            if (byteCount != expected || pdu.Length != 6 + expected) return Error(3);
            if (offset < 0 || offset + quantity > (fn == 15 ? coils.Length : holding.Length)) return Error(2);
            if (fn == 15) for (var i = 0; i < quantity; i++) coils[offset + i] = (pdu[6 + i / 8] & 1 << (i % 8)) != 0;
            else for (var i = 0; i < quantity; i++) holding[offset + i] = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(6 + i * 2, 2));
            return pdu[..5].ToArray();
        }
    }
}

internal sealed class ModbusTcpSimulator : IDisposable
{
    private readonly ConcurrentDictionary<int, TcpClient> clients = new();
    private readonly IReadOnlyDictionary<byte, ModbusTcpUnit> units;
    private CancellationTokenSource? cts;
    private TcpListener? listener;
    private int nextClient;
    public event Action<string>? Log;
    public IPEndPoint? LocalEndpoint => listener?.LocalEndpoint as IPEndPoint;
    public bool Running => cts is not null;
    public ModbusTcpSimulator(IEnumerable<ModbusTcpUnit> units)
    {
        var list = units.ToArray();
        if (list.Length is < 1 or > 32 || list.Select(x => x.UnitId).Distinct().Count() != list.Length)
            throw new ArgumentException("请配置 1–32 个地址不重复的虚拟从站。");
        this.units = list.ToDictionary(x => x.UnitId);
    }
    public void Start(IPAddress address, int port)
    {
        if (Running) throw new InvalidOperationException("虚拟从站已启动。");
        if (address.AddressFamily != AddressFamily.InterNetwork || port is < 0 or > 65535) throw new ArgumentException("监听地址或端口无效。");
        var socket = new TcpListener(address, port); socket.Start(16);
        listener = socket; cts = new CancellationTokenSource();
        _ = AcceptAsync(socket, cts.Token);
        Log?.Invoke($"监听 {socket.LocalEndpoint}；{units.Count} 个虚拟从站");
    }
    private async Task AcceptAsync(TcpListener socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await socket.AcceptTcpClientAsync(token).ConfigureAwait(false);
                if (clients.Count >= 16) { client.Dispose(); continue; }
                var id = Interlocked.Increment(ref nextClient); clients[id] = client;
                _ = ServeAsync(id, client, token);
            }
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        { if (!token.IsCancellationRequested) Log?.Invoke("监听失败：" + ex.Message); }
    }
    private async Task ServeAsync(int clientId, TcpClient client, CancellationToken token)
    {
        var peer = client.Client.RemoteEndPoint?.ToString() ?? "客户端";
        try
        {
            using var stream = client.GetStream();
            var header = new byte[6];
            while (!token.IsCancellationRequested)
            {
                await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
                var protocol = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4));
                if (protocol != 0 || length is < 2 or > 254) { Log?.Invoke($"{peer} MBAP 无效，已断开"); break; }
                var body = new byte[length]; await stream.ReadExactlyAsync(body, token).ConfigureAwait(false);
                var unit = body[0];
                if (!units.TryGetValue(unit, out var slave)) { Log?.Invoke($"{peer} Unit {unit} 不存在，未响应"); continue; }
                var response = slave.Process(body.AsSpan(1));
                var output = new byte[7 + response.Length]; header.AsSpan(0, 4).CopyTo(output);
                BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), checked((ushort)(response.Length + 1)));
                output[6] = unit; response.CopyTo(output, 7);
                await stream.WriteAsync(output, token).ConfigureAwait(false);
                Log?.Invoke($"{peer} Unit {unit} FC {body[1]:X2} → {((response[0] & 0x80) != 0 ? $"异常 {response[1]:X2}" : "OK")}");
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        { if (!token.IsCancellationRequested && ex is not EndOfStreamException) Log?.Invoke($"{peer} 连接结束：{ex.Message}"); }
        finally { clients.TryRemove(clientId, out _); client.Dispose(); }
    }
    public void Stop()
    {
        var old = cts; cts = null; old?.Cancel(); listener?.Stop(); listener = null;
        foreach (var client in clients.Values) client.Dispose(); clients.Clear(); old?.Dispose();
    }
    public void Dispose() => Stop();
}
