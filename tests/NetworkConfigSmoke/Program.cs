using System.Net;
using System.Net.Sockets;
using IndustrialFieldDataAcquisitionTerminal;

using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
var payload = new byte[] { 192,168,1,50, 255,255,255,0, 192,168,1,1, 0x13,0x89 };
var server = Task.Run(async () =>
{
    using var socket = await listener.AcceptTcpClientAsync();
    var s = socket.GetStream();
    for (var cmd = 1; cmd <= 2; cmd++)
    {
        var header = new byte[7]; await s.ReadExactlyAsync(header);
        var body = new byte[((header[4] << 8) | header[5]) - 1]; await s.ReadExactlyAsync(body);
        if (header[6] != 1 || body[0] != 0x42 || body[1] != cmd ||
            (cmd == 1 && body.Length != 2) || (cmd == 2 && !body.AsSpan(2).SequenceEqual(payload)))
            throw new Exception("incorrect network configuration request");
        var response = cmd == 1 ? new byte[] {0x42,1,0}.Concat(payload).ToArray() : new byte[] {0x42,2,0};
        header[4] = 0; header[5] = (byte)(response.Length + 1);
        await s.WriteAsync(header); await s.WriteAsync(response);
    }
});
using var client = new ModbusTcpClient { ReadTimeoutMs = 2000 };
await client.OpenAsync("127.0.0.1", port, 1, CancellationToken.None);
var read = client.TransactCustom(0x42, 1, Array.Empty<byte>(), 18);
if (read[3] != 0 || !read.AsSpan(4).SequenceEqual(payload)) throw new Exception("incorrect read response");
var write = client.TransactCustom(0x42, 2, payload, 4);
if (!write.SequenceEqual(new byte[] {1,0x42,2,0})) throw new Exception("incorrect write response");
await server;
Console.WriteLine("PASS: network settings request and response over TCP");
