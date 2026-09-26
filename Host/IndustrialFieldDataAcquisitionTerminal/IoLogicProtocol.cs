using System.Buffers.Binary;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed record IoLogicDeviceInfo(
    ushort FormatVersion,
    byte RunState,
    byte ConfigState,
    ushort RuleCount,
    uint ImageLength,
    uint ImageCrc32,
    uint Generation,
    ushort ScanPeriodMs,
    ushort LastError);

internal sealed class IoLogicProtocol
{
    private const byte Function = 0x41;
    // GET_INFO: 26 字节协议载荷 + 2 字节 Modbus CRC。
    private const int GetInfoResponseLength = 28;
    private const byte CmdGetInfo = 0x01;
    private const byte CmdBegin = 0x02;
    private const byte CmdData = 0x03;
    private const byte CmdVerify = 0x04;
    private const byte CmdActivate = 0x05;
    private const byte CmdReadData = 0x06;
    private const byte CmdAbort = 0x07;
    private const byte CmdClear = 0x08;
    private const byte CmdRun = 0x09;
    private const byte CmdStop = 0x0A;

    private readonly ModbusRtuClient client;

    public IoLogicProtocol(ModbusRtuClient client)
    {
        this.client = client;
    }

    public IoLogicDeviceInfo ReadInfo()
    {
        var response = Request(CmdGetInfo, Array.Empty<byte>(), GetInfoResponseLength);
        return new IoLogicDeviceInfo(
            ReadU16(response, 4), response[6], response[7], ReadU16(response, 8),
            ReadU32(response, 10), ReadU32(response, 14), ReadU32(response, 18),
            ReadU16(response, 22), ReadU16(response, 24));
    }

    public ushort BeginDownload(ushort session, IoLogicCompiledImage image)
    {
        var payload = new byte[17];
        payload[0] = CmdBegin;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1, 2), session);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(3, 2), IoLogicBinaryFormat.FormatVersion);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(5, 2), 0x0003);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(7, 4), (uint)image.Bytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(11, 4), IoLogicBinaryFormat.Crc32(image.Bytes));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(15, 2), image.RuleCount);

        var response = Request(CmdBegin, payload[1..], 10);
        EnsureSession(response, session);
        return ReadU16(response, 6);
    }

    public void SendData(ushort session, uint offset, ReadOnlySpan<byte> data)
    {
        if (data.Length is < 1 or > IoLogicBinaryFormat.MaxDataChunk)
        {
            throw new ArgumentOutOfRangeException(nameof(data));
        }

        var payload = new byte[8 + data.Length];
        payload[0] = CmdData;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1, 2), session);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(3, 4), offset);
        payload[7] = (byte)data.Length;
        data.CopyTo(payload.AsSpan(8));

        var response = Request(CmdData, payload[1..], 14);
        EnsureSession(response, session);
        if (ReadU32(response, 6) != offset || ReadU16(response, 10) != data.Length)
        {
            throw new ModbusException("MCU 返回的下载偏移或接收长度不匹配。");
        }
    }

    public void Verify(ushort session, IoLogicCompiledImage image)
    {
        var data = new byte[6];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0, 2), session);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(2, 4), IoLogicBinaryFormat.Crc32(image.Bytes));
        var response = Request(CmdVerify, data, 14);
        if (ReadU32(response, 4) != (uint)image.Bytes.Length
            || ReadU32(response, 8) != IoLogicBinaryFormat.Crc32(image.Bytes))
        {
            throw new ModbusException("MCU VERIFY 返回的长度或 CRC32 不匹配。");
        }
    }

    public void Activate(ushort session)
    {
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(data, session);
        var response = Request(CmdActivate, data, 11, 3000);
        if (response.Length < 11)
        {
            throw new ModbusException("MCU ACTIVATE 响应长度错误。");
        }
    }

    public byte[] ReadImageChunk(uint offset, byte length)
    {
        if (length is 0 or > IoLogicBinaryFormat.MaxDataChunk)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var data = new byte[5];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), offset);
        data[4] = length;
        var response = Request(CmdReadData, data, 11 + length);
        var responseOffset = ReadU32(response, 4);
        var responseLength = response[8];
        if (responseOffset != offset || responseLength != length)
        {
            throw new ModbusException("MCU 回读配置数据的偏移或长度不匹配。");
        }

        return response.AsSpan(9, responseLength).ToArray();
    }

    public void Abort(ushort session) => SendSessionCommand(CmdAbort, session, 6);

    public void Clear() => SendSimpleCommand(CmdClear);

    public void Run() => SendSimpleCommand(CmdRun);

    public void Stop() => SendSimpleCommand(CmdStop);

    private void SendSessionCommand(byte command, ushort session, int responseLength)
    {
        var data = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(data, session);
        Request(command, data, responseLength);
    }

    private void SendSimpleCommand(byte command) => Request(command, Array.Empty<byte>(), 6);

    private byte[] Request(byte command,
                           ReadOnlySpan<byte> data,
                           int expectedLength,
                           int? responseTimeoutMs = null)
    {
        var response = client.TransactCustom(Function, command, data, expectedLength, responseTimeoutMs);
        if (response.Length < 4 || response[2] != command)
        {
            throw new ModbusException($"IOCF 响应子命令错误：期望 0x{command:X2}。");
        }

        var status = response[3];
        if (status != 0)
        {
            throw new ModbusException($"MCU 拒绝 IOCF 操作：子命令=0x{command:X2}，状态码=0x{status:X2}（{DescribeStatus(status)}）。");
        }

        return response;
    }

    private static void EnsureSession(byte[] response, ushort session)
    {
        if (response.Length >= 6 && ReadU16(response, 4) != session)
        {
            throw new ModbusException("MCU 返回的下载会话号不匹配。");
        }
    }

    private static ushort ReadU16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2));
    private static uint ReadU32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static string DescribeStatus(byte status) => status switch
    {
        0x01 => "设备忙",
        0x02 => "会话不存在",
        0x03 => "偏移错误",
        0x04 => "长度错误",
        0x05 => "CRC 错误",
        0x06 => "格式版本不支持",
        0x07 => "程序大小超限",
        0x08 => "规则数量超限",
        0x09 => "规则内容非法",
        0x0A => "Flash 写入失败",
        0x0B => "Flash 回读校验失败",
        0x0D => "当前状态禁止操作",
        0x0E => "下载超时自动取消",
        _ => "未知错误"
    };
}

internal sealed class IoLogicDownloadService
{
    private readonly IoLogicProtocol protocol;
    private readonly Action<string> log;

    public IoLogicDownloadService(IoLogicProtocol protocol, Action<string> log)
    {
        this.protocol = protocol;
        this.log = log;
    }

    public IoLogicDeviceInfo Download(IoLogicCompiledImage image, CancellationToken cancellationToken)
    {
        var before = protocol.ReadInfo();
        var generation = Math.Max(before.Generation + 1, image.Generation);
        var session = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var actualImage = image with
        {
            Bytes = IoLogicBinaryFormat.WithGeneration(image.Bytes, generation),
            Generation = generation
        };
        try
        {
            var beginBlock = protocol.BeginDownload(session, actualImage);
            log($"IOCF BEGIN 成功：session=0x{session:X4}, size={actualImage.Bytes.Length}, block={beginBlock}");

            for (var offset = 0; offset < actualImage.Bytes.Length; offset += beginBlock)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(beginBlock, actualImage.Bytes.Length - offset);
                protocol.SendData(session, (uint)offset, actualImage.Bytes.AsSpan(offset, length));
                log($"IOCF DATA：{offset + length}/{actualImage.Bytes.Length} 字节");
            }

            protocol.Verify(session, actualImage);
            log("IOCF VERIFY 成功：整包 CRC32 校验通过");
            protocol.Activate(session);
            log("IOCF ACTIVATE 成功：新配置已切换");
            var after = protocol.ReadInfo();
            log($"IOCF GET_INFO：generation={after.Generation}, slot={after.ConfigState}, rules={after.RuleCount}");
            return after;
        }
        catch
        {
            try
            {
                protocol.Abort(session);
            }
            catch
            {
                // 保留原始下载错误；MCU 仍会继续使用旧配置。
            }

            throw;
        }
    }
}
