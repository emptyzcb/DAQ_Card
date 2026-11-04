using System.Net;
using System.Net.Sockets;
using IndustrialFieldDataAcquisitionTerminal;

var request = ModbusTcpAdu.BuildRequest(
    0x1234,
    1,
    new byte[] { 0x03, 0x00, 0x10, 0x00, 0x02 });
var expectedRequest = new byte[]
{
    0x12, 0x34, 0x00, 0x00, 0x00, 0x06, 0x01,
    0x03, 0x00, 0x10, 0x00, 0x02
};
if (!request.SequenceEqual(expectedRequest))
{
    throw new InvalidOperationException("Modbus TCP MBAP request framing is incorrect.");
}

var readResponse = new byte[]
{
    0x12, 0x34, 0x00, 0x00, 0x00, 0x07, 0x01,
    0x03, 0x04, 0x11, 0x22, 0x33, 0x44
};
var pdu = ModbusTcpAdu.ValidateAndExtractResponse(readResponse, 0x1234, 1);
if (!pdu.SequenceEqual(new byte[] { 0x03, 0x04, 0x11, 0x22, 0x33, 0x44 }))
{
    throw new InvalidOperationException("Modbus TCP response PDU extraction is incorrect.");
}

var writeResponse = new byte[]
{
    0x12, 0x35, 0x00, 0x00, 0x00, 0x06, 0x01,
    0x06, 0x02, 0x84, 0x00, 0x01
};
var writePdu = ModbusTcpAdu.ValidateAndExtractResponse(writeResponse, 0x1235, 1);
if (writePdu.Length != 5 || writePdu[0] != 0x06)
{
    throw new InvalidOperationException("Function 0x06 response must not be parsed as a byte-count response.");
}

try
{
    ModbusTcpAdu.ValidateAndExtractResponse(readResponse, 0x9999, 1);
    throw new InvalidOperationException("Transaction ID mismatch was not rejected.");
}
catch (ModbusException)
{
}

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
var serverTask = RunFakeBoardAsync(listener);

using (var client = new ModbusTcpClient { ReadTimeoutMs = 2000 })
{
    await client.OpenAsync(IPAddress.Loopback.ToString(), port, 1, CancellationToken.None);
    if (client.SocketAddressFamily != AddressFamily.InterNetwork)
    {
        throw new InvalidOperationException("Modbus TCP client must use an IPv4 socket.");
    }

    var values = client.ReadHoldingRegisters(0x0010, 2);
    if (!values.SequenceEqual(new ushort[] { 0x1122, 0x3344 }))
    {
        throw new InvalidOperationException("Fragmented holding-register response was decoded incorrectly.");
    }

    client.WriteSingleRegister(0x0284, 1);

    var custom = client.TransactCustom(0x41, 0x01, ReadOnlySpan<byte>.Empty, 4);
    if (!custom.SequenceEqual(new byte[] { 0x01, 0x41, 0x01, 0x00 }))
    {
        throw new InvalidOperationException("IOCF Modbus TCP response was decoded incorrectly.");
    }

    try
    {
        client.ReadHoldingRegisters(0xFFFF, 1);
        throw new InvalidOperationException("Modbus exception response was not rejected.");
    }
    catch (ModbusException ex) when (ex.Message.Contains("0x02", StringComparison.Ordinal))
    {
    }
}

await serverTask;
using (var cancelledClient = new ModbusTcpClient())
{
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try
    {
        await cancelledClient.OpenAsync(IPAddress.Loopback.ToString(), port, 1, cancelled.Token);
        throw new InvalidOperationException("A cancelled connection attempt unexpectedly succeeded.");
    }
    catch (OperationCanceledException)
    {
        if (cancelledClient.IsOpen)
            throw new InvalidOperationException("A cancelled connection left the socket open.");
    }
}
Console.WriteLine("Modbus TCP codec and socket smoke tests passed.");

static async Task RunFakeBoardAsync(TcpListener listener)
{
    try
    {
        using var socket = await listener.AcceptTcpClientAsync();
        using var stream = socket.GetStream();

        var readRequest = await ReadAduAsync(stream);
        RequirePdu(readRequest, new byte[] { 0x03, 0x00, 0x10, 0x00, 0x02 });
        var fragmentedResponse = BuildResponse(readRequest, new byte[] { 0x03, 0x04, 0x11, 0x22, 0x33, 0x44 });
        await stream.WriteAsync(fragmentedResponse.AsMemory(0, 3));
        await Task.Delay(10);
        await stream.WriteAsync(fragmentedResponse.AsMemory(3, 4));
        await Task.Delay(10);
        await stream.WriteAsync(fragmentedResponse.AsMemory(7));

        var writeRequest = await ReadAduAsync(stream);
        RequirePdu(writeRequest, new byte[] { 0x06, 0x02, 0x84, 0x00, 0x01 });
        await stream.WriteAsync(BuildResponse(writeRequest, writeRequest.AsSpan(7).ToArray()));

        var customRequest = await ReadAduAsync(stream);
        RequirePdu(customRequest, new byte[] { 0x41, 0x01 });
        await stream.WriteAsync(BuildResponse(customRequest, new byte[] { 0x41, 0x01, 0x00 }));

        var invalidReadRequest = await ReadAduAsync(stream);
        RequirePdu(invalidReadRequest, new byte[] { 0x03, 0xFF, 0xFF, 0x00, 0x01 });
        await stream.WriteAsync(BuildResponse(invalidReadRequest, new byte[] { 0x83, 0x02 }));
    }
    finally
    {
        listener.Stop();
    }
}

static async Task<byte[]> ReadAduAsync(NetworkStream stream)
{
    var header = new byte[7];
    await stream.ReadExactlyAsync(header);
    var declaredLength = (header[4] << 8) | header[5];
    if (declaredLength is < 2 or > 254)
    {
        throw new InvalidOperationException($"Fake board received invalid MBAP length {declaredLength}.");
    }

    var body = new byte[declaredLength - 1];
    await stream.ReadExactlyAsync(body);
    return header.Concat(body).ToArray();
}

static byte[] BuildResponse(byte[] requestAdu, byte[] responsePdu)
{
    var response = new byte[7 + responsePdu.Length];
    response[0] = requestAdu[0];
    response[1] = requestAdu[1];
    response[4] = (byte)((responsePdu.Length + 1) >> 8);
    response[5] = (byte)(responsePdu.Length + 1);
    response[6] = requestAdu[6];
    responsePdu.CopyTo(response, 7);
    return response;
}

static void RequirePdu(byte[] requestAdu, byte[] expectedPdu)
{
    if (!requestAdu.AsSpan(7).SequenceEqual(expectedPdu))
    {
        throw new InvalidOperationException(
            $"Fake board received unexpected PDU: {Convert.ToHexString(requestAdu.AsSpan(7))}.");
    }
}
