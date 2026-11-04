using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class ModbusException : Exception
{
    public ModbusException(string message) : base(message)
    {
    }
}

internal interface IModbusRegisterClient
{
    bool IsOpen { get; }
    ushort[] ReadHoldingRegisters(ushort startAddress, ushort quantity);
    void WriteSingleRegister(ushort address, ushort value);
}

internal static class ModbusTcpAdu
{
    private const int MbapHeaderLength = 7;
    private const int MaxPduLength = 253;

    public static byte[] BuildRequest(ushort transactionId, byte unitId, ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length is < 1 or > MaxPduLength)
        {
            throw new ArgumentOutOfRangeException(nameof(pdu));
        }

        var adu = new byte[MbapHeaderLength + pdu.Length];
        WriteUInt16(adu, 0, transactionId);
        WriteUInt16(adu, 2, 0);
        WriteUInt16(adu, 4, checked((ushort)(pdu.Length + 1)));
        adu[6] = unitId;
        pdu.CopyTo(adu.AsSpan(MbapHeaderLength));
        return adu;
    }

    public static byte[] ValidateAndExtractResponse(byte[] adu,
                                                    ushort expectedTransactionId,
                                                    byte expectedUnitId)
    {
        if (adu.Length < MbapHeaderLength + 1)
        {
            throw new ModbusException("Modbus TCP 响应长度不足。");
        }

        var transactionId = ReadUInt16(adu, 0);
        var protocolId = ReadUInt16(adu, 2);
        var declaredLength = ReadUInt16(adu, 4);
        if (transactionId != expectedTransactionId)
        {
            throw new ModbusException($"Modbus TCP 事务号不匹配：期望 0x{expectedTransactionId:X4}，收到 0x{transactionId:X4}。");
        }
        if (protocolId != 0)
        {
            throw new ModbusException($"Modbus TCP 协议标识错误：0x{protocolId:X4}。");
        }
        if (declaredLength != adu.Length - 6)
        {
            throw new ModbusException($"Modbus TCP MBAP 长度错误：声明 {declaredLength}，实际 {adu.Length - 6}。");
        }
        if (adu[6] != expectedUnitId)
        {
            throw new ModbusException($"Modbus 单元标识错误：期望 {expectedUnitId}，收到 {adu[6]}。");
        }

        return adu.AsSpan(MbapHeaderLength).ToArray();
    }

    public static byte[] ReadResponse(NetworkStream stream,
                                      ushort expectedTransactionId,
                                      byte expectedUnitId)
    {
        var header = ReadExact(stream, MbapHeaderLength);
        var declaredLength = ReadUInt16(header, 4);
        if (declaredLength is < 2 or > 254)
        {
            throw new ModbusException($"Modbus TCP MBAP 长度越界：{declaredLength}。");
        }

        var body = ReadExact(stream, declaredLength - 1);
        var adu = new byte[MbapHeaderLength + body.Length];
        header.CopyTo(adu, 0);
        body.CopyTo(adu, MbapHeaderLength);
        return ValidateAndExtractResponse(adu, expectedTransactionId, expectedUnitId);
    }

    private static byte[] ReadExact(NetworkStream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new IOException("以太网连接已被板卡关闭。");
            }
            offset += read;
        }
        return buffer;
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
    }
}

/// <summary>
/// Standard Modbus TCP client. MBAP framing is used on Ethernet; CRC is used only on RS485 RTU.
/// </summary>
internal sealed class ModbusTcpClient : IModbusRegisterClient, IDisposable
{
    private readonly object sync = new();
    private TcpClient? tcp;
    private NetworkStream? stream;
    private ushort nextTransactionId = 1;

    public byte UnitId { get; private set; } = 1;
    public bool IsOpen => tcp?.Connected == true && stream != null;
    internal AddressFamily? SocketAddressFamily => tcp?.Client.AddressFamily;
    public int ReadTimeoutMs { get; set; } = 1000;

    public event Action<string>? LogMessage;

    public async Task OpenAsync(string host, int port, byte unitId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Close();

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("请输入板卡 IP 地址。", nameof(host));
        }
        if (unitId is 0 or > 247)
        {
            throw new ArgumentOutOfRangeException(nameof(unitId), "Modbus 单元标识必须在 1~247。");
        }

        var remoteAddress = await ResolveIpv4AddressAsync(host, cancellationToken).ConfigureAwait(false);
        var client = new TcpClient(AddressFamily.InterNetwork)
        {
            ReceiveTimeout = ReadTimeoutMs,
            SendTimeout = ReadTimeoutMs,
            NoDelay = true
        };

        try
        {
            var localAddress = FindMatchingLocalAddress(remoteAddress);
            if (localAddress != null)
            {
                client.Client.Bind(new IPEndPoint(localAddress, 0));
            }

            await client.ConnectAsync(remoteAddress, port, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            tcp = client;
            stream = client.GetStream();
            UnitId = unitId;
            Log($"以太网已连接：{remoteAddress}:{port}, 本机={client.Client.LocalEndPoint}, Unit ID={unitId}");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task<IPAddress> ResolveIpv4AddressAsync(string host, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var parsed))
        {
            if (parsed.AddressFamily == AddressFamily.InterNetwork)
            {
                return parsed;
            }
            if (parsed.IsIPv4MappedToIPv6)
            {
                return parsed.MapToIPv4();
            }
            throw new ArgumentException("板卡地址必须是 IPv4 地址。", nameof(host));
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        var address = addresses
            .FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork);
        return address ?? throw new SocketException((int)SocketError.HostNotFound);
    }

    private static IPAddress? FindMatchingLocalAddress(IPAddress remoteAddress)
    {
        if (IPAddress.IsLoopback(remoteAddress))
        {
            return IPAddress.Loopback;
        }

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                     .OrderBy(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1))
        {
            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    unicast.IPv4Mask != null &&
                    IsSameSubnet(unicast.Address, remoteAddress, unicast.IPv4Mask))
                {
                    return unicast.Address;
                }
            }
        }

        return null;
    }

    private static bool IsSameSubnet(IPAddress localAddress, IPAddress remoteAddress, IPAddress mask)
    {
        var localBytes = localAddress.GetAddressBytes();
        var remoteBytes = remoteAddress.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        for (var index = 0; index < localBytes.Length; index++)
        {
            if ((localBytes[index] & maskBytes[index]) !=
                (remoteBytes[index] & maskBytes[index]))
            {
                return false;
            }
        }

        return true;
    }

    public void Close()
    {
        lock (sync)
        {
            try
            {
                stream?.Close();
                tcp?.Close();
            }
            finally
            {
                stream = null;
                tcp = null;
            }
        }
    }

    public ushort[] ReadHoldingRegisters(ushort startAddress, ushort quantity)
    {
        if (quantity is 0 or > 125)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        var requestPdu = new byte[5];
        requestPdu[0] = 0x03;
        WriteUInt16(requestPdu, 1, startAddress);
        WriteUInt16(requestPdu, 3, quantity);
        var responsePdu = TransactPdu(requestPdu);

        var expectedByteCount = quantity * 2;
        if (responsePdu.Length < 2 ||
            responsePdu[1] != expectedByteCount ||
            responsePdu.Length != expectedByteCount + 2)
        {
            throw new ModbusException($"读寄存器响应长度错误：byteCount={responsePdu.ElementAtOrDefault(1)}。");
        }

        var values = new ushort[quantity];
        for (var index = 0; index < quantity; index++)
        {
            values[index] = ReadUInt16(responsePdu, 2 + index * 2);
        }
        return values;
    }

    public void WriteSingleRegister(ushort address, ushort value)
    {
        var requestPdu = new byte[5];
        requestPdu[0] = 0x06;
        WriteUInt16(requestPdu, 1, address);
        WriteUInt16(requestPdu, 3, value);
        var responsePdu = TransactPdu(requestPdu);
        if (!responsePdu.AsSpan().SequenceEqual(requestPdu))
        {
            throw new ModbusException("写单寄存器响应内容不匹配。");
        }
    }

    public byte[] TransactCustom(byte function,
                                 byte command,
                                 ReadOnlySpan<byte> payload,
                                 int expectedResponseLength,
                                 int? responseTimeoutMs = null)
    {
        if (expectedResponseLength is < 4 or > 254)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedResponseLength));
        }

        var requestPdu = new byte[2 + payload.Length];
        requestPdu[0] = function;
        requestPdu[1] = command;
        payload.CopyTo(requestPdu.AsSpan(2));
        var responsePdu = TransactPdu(requestPdu, responseTimeoutMs);

        if (responsePdu.Length < 3 || responsePdu[1] != command)
        {
            throw new ModbusException($"IOCF 响应子命令错误：期望 0x{command:X2}。");
        }

        var logicalResponse = new byte[responsePdu.Length + 1];
        logicalResponse[0] = UnitId;
        responsePdu.CopyTo(logicalResponse, 1);
        if (responsePdu[2] == 0 && logicalResponse.Length != expectedResponseLength)
        {
            throw new ModbusException($"IOCF 响应长度错误：期望 {expectedResponseLength}，收到 {logicalResponse.Length}。");
        }
        return logicalResponse;
    }

    private byte[] TransactPdu(byte[] requestPdu, int? responseTimeoutMs = null)
    {
        lock (sync)
        {
            var s = stream;
            if (s is null || tcp is not { Connected: true })
            {
                throw new InvalidOperationException("以太网尚未连接。");
            }

            s.ReadTimeout = responseTimeoutMs ?? ReadTimeoutMs;
            var transactionId = nextTransactionId++;
            var requestAdu = ModbusTcpAdu.BuildRequest(transactionId, UnitId, requestPdu);
            s.Write(requestAdu, 0, requestAdu.Length);
            Log($"TX  {ToHex(requestAdu)}");

            var responsePdu = ModbusTcpAdu.ReadResponse(s, transactionId, UnitId);
            Log($"RX  TID=0x{transactionId:X4} UNIT={UnitId} PDU={ToHex(responsePdu)}");

            var requestFunction = requestPdu[0];
            if (responsePdu[0] == (byte)(requestFunction | 0x80))
            {
                var exception = responsePdu.ElementAtOrDefault(1);
                throw new ModbusException($"设备返回 Modbus 异常：功能码=0x{requestFunction:X2}, 异常码=0x{exception:X2}。");
            }
            if (responsePdu[0] != requestFunction)
            {
                throw new ModbusException($"响应功能码错误：0x{responsePdu[0]:X2}。");
            }
            return responsePdu;
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"{DateTime.Now:HH:mm:ss.fff}  {message}");
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
    }

    private static string ToHex(ReadOnlySpan<byte> data)
    {
        return Convert.ToHexString(data).InsertEvery(2, ' ');
    }

    public void Dispose()
    {
        Close();
    }
}

internal static class StringExtensions
{
    public static string InsertEvery(this string value, int interval, char separator)
    {
        if (value.Length <= interval)
        {
            return value;
        }

        var result = new System.Text.StringBuilder(value.Length + value.Length / interval);
        for (var index = 0; index < value.Length; index += interval)
        {
            if (index > 0)
            {
                result.Append(separator);
            }
            result.Append(value, index, Math.Min(interval, value.Length - index));
        }
        return result.ToString();
    }
}
