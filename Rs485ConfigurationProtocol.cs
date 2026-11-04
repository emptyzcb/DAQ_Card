namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed record Rs485SensorConfig(
    string Name,
    bool Enabled,
    byte SlaveAddress,
    byte Function,
    ushort StartRegister,
    ushort Quantity,
    ushort IntervalMs,
    int BaudRate = 115200,
    string Parity = "None",
    int StopBits = 1);

internal sealed record Rs485SensorTelemetry(
    int Index,
    byte SlaveAddress,
    byte Function,
    byte Quantity,
    bool Configured,
    bool Enabled,
    bool LastOk,
    uint ErrorCount,
    uint TimestampMs,
    ushort[] Values);

internal sealed class Rs485ConfigurationProtocol
{
    public const int MaximumItems = 32;
    public const int RegistersPerItem = 16;
    public const ushort MinimumIntervalMs = 20;
    public const ushort MaximumIntervalMs = 60000;

    private const ushort ActiveCountRegister = 0x0280;
    private const ushort CapacityRegister = 0x0281;
    private const ushort StagedCountRegister = 0x0284;
    private const ushort ApplyRegister = 0x0285;
    private const ushort ApplyMagic = 0xA55A;
    private const ushort DataBase = 0x0080;
    private const ushort MetadataBase = 0x0300;
    private const ushort StagingBase = 0x0400;
    private const int MetadataRegistersPerItem = 8;
    private const int StagingRegistersPerItem = 6;

    private readonly IModbusRegisterClient client;

    public Rs485ConfigurationProtocol(IModbusRegisterClient client)
    {
        this.client = client;
    }

    public void Download(IReadOnlyList<Rs485SensorConfig> entries)
    {
        Validate(entries);
        EnsureConnected();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var baseAddress = checked((ushort)(StagingBase + index * StagingRegistersPerItem));
            client.WriteSingleRegister(baseAddress, entry.Enabled ? (ushort)1 : (ushort)0);
            client.WriteSingleRegister((ushort)(baseAddress + 1),
                (ushort)((entry.Function << 8) | entry.SlaveAddress));
            client.WriteSingleRegister((ushort)(baseAddress + 2), entry.StartRegister);
            client.WriteSingleRegister((ushort)(baseAddress + 3), entry.Quantity);
            client.WriteSingleRegister((ushort)(baseAddress + 4), entry.IntervalMs);
            client.WriteSingleRegister((ushort)(baseAddress + 5), EncodeSerialSettings(entry));
            if (client.ReadHoldingRegisters((ushort)(baseAddress + 5), 1)[0] != EncodeSerialSettings(entry))
                throw new ModbusException("板卡未接受串口参数，请更新板卡固件后重试。");
        }

        client.WriteSingleRegister(StagedCountRegister, checked((ushort)entries.Count));
        client.WriteSingleRegister(ApplyRegister, ApplyMagic);

        var status = client.ReadHoldingRegisters(ActiveCountRegister, 4);
        if (status.Length != 4 || status[0] != entries.Count || status[1] < entries.Count)
        {
            throw new ModbusException("RS485 采集表已发送，但板卡返回的活动项数不匹配。");
        }
    }

    public IReadOnlyList<Rs485SensorConfig> ReadStagedConfiguration()
    {
        EnsureConnected();
        var count = client.ReadHoldingRegisters(StagedCountRegister, 1)[0];
        if (count > MaximumItems)
        {
            throw new ModbusException($"板卡返回的 RS485 配置数量越界：{count}。");
        }

        var raw = ReadRange(StagingBase, count * StagingRegistersPerItem);
        var entries = new List<Rs485SensorConfig>(count);
        for (var index = 0; index < count; index++)
        {
            var offset = index * StagingRegistersPerItem;
            var identity = raw[offset + 1];
            var serial = DecodeSerialSettings(raw[offset + 5]);
            entries.Add(new Rs485SensorConfig(
                $"传感器 {index + 1}",
                raw[offset] != 0,
                (byte)identity,
                (byte)(identity >> 8),
                raw[offset + 2],
                raw[offset + 3],
                raw[offset + 4], serial.BaudRate, serial.Parity, serial.StopBits));
        }
        return entries;
    }

    public IReadOnlyList<Rs485SensorTelemetry> ReadTelemetry()
    {
        EnsureConnected();
        var activeCount = client.ReadHoldingRegisters(ActiveCountRegister, 1)[0];
        if (activeCount > MaximumItems)
        {
            throw new ModbusException($"板卡返回的 RS485 活动项数越界：{activeCount}。");
        }
        if (activeCount == 0)
        {
            return Array.Empty<Rs485SensorTelemetry>();
        }

        var metadata = ReadRange(MetadataBase, activeCount * MetadataRegistersPerItem);
        var values = ReadRange(DataBase, activeCount * RegistersPerItem);
        var result = new List<Rs485SensorTelemetry>(activeCount);
        for (var index = 0; index < activeCount; index++)
        {
            var metadataOffset = index * MetadataRegistersPerItem;
            var valueOffset = index * RegistersPerItem;
            var flags = metadata[metadataOffset + 3];
            var quantity = (byte)Math.Min((int)metadata[metadataOffset + 2], RegistersPerItem);
            var itemValues = values.AsSpan(valueOffset, quantity).ToArray();
            result.Add(new Rs485SensorTelemetry(
                index,
                (byte)metadata[metadataOffset],
                (byte)metadata[metadataOffset + 1],
                quantity,
                (flags & 0x0001) != 0,
                (flags & 0x0002) != 0,
                (flags & 0x0004) != 0,
                JoinUInt32(metadata[metadataOffset + 4], metadata[metadataOffset + 5]),
                JoinUInt32(metadata[metadataOffset + 6], metadata[metadataOffset + 7]),
                itemValues));
        }
        return result;
    }

    public static void Validate(IReadOnlyList<Rs485SensorConfig> entries)
    {
        if (entries.Count > MaximumItems)
        {
            throw new ArgumentException($"RS485 采集项最多 {MaximumItems} 个。", nameof(entries));
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var prefix = $"第 {index + 1} 项";
            _ = EncodeSerialSettings(entry);
            if (entry.SlaveAddress is 0 or > 247)
            {
                throw new ArgumentException($"{prefix}从站地址必须在 1~247。", nameof(entries));
            }
            if (entry.Function is not (0x03 or 0x04))
            {
                throw new ArgumentException($"{prefix}功能码只能是 03 或 04。", nameof(entries));
            }
            if (entry.Quantity is 0 or > RegistersPerItem)
            {
                throw new ArgumentException($"{prefix}采集寄存器数量必须在 1~{RegistersPerItem}。", nameof(entries));
            }
            if (entry.IntervalMs is < MinimumIntervalMs or > MaximumIntervalMs)
            {
                throw new ArgumentException($"{prefix}轮询周期必须在 {MinimumIntervalMs}~{MaximumIntervalMs} ms。", nameof(entries));
            }
        }
    }

    private static readonly int[] SupportedBaudRates = { 115200, 1200, 2400, 4800, 9600, 19200, 38400, 57600 };
    public static ushort EncodeSerialSettings(Rs485SensorConfig entry)
    {
        var baudIndex = Array.IndexOf(SupportedBaudRates, entry.BaudRate);
        var parity = entry.Parity switch { "None" => 0, "Even" => 1, "Odd" => 2, _ => -1 };
        if (baudIndex < 0 || parity < 0 || entry.StopBits is not (1 or 2))
            throw new ArgumentException("RS485 串口参数无效：请选择支持的波特率、校验位和停止位。");
        return (ushort)(baudIndex | (parity << 4) | (entry.StopBits == 2 ? 0x40 : 0));
    }

    public static (int BaudRate, string Parity, int StopBits) DecodeSerialSettings(ushort value)
    {
        var baud = value & 0x0F;
        var parity = (value >> 4) & 3;
        if ((value & 0xFF80) != 0 || baud >= SupportedBaudRates.Length || parity > 2)
            throw new ModbusException($"板卡返回了无效的串口配置：0x{value:X4}");
        return (SupportedBaudRates[baud], parity == 1 ? "Even" : parity == 2 ? "Odd" : "None",
                (value & 0x40) != 0 ? 2 : 1);
    }

    private ushort[] ReadRange(ushort startAddress, int count)
    {
        if (count == 0)
        {
            return Array.Empty<ushort>();
        }

        var result = new ushort[count];
        var offset = 0;
        while (offset < count)
        {
            var chunk = Math.Min(125, count - offset);
            var values = client.ReadHoldingRegisters(
                checked((ushort)(startAddress + offset)),
                checked((ushort)chunk));
            if (values.Length != chunk)
            {
                throw new ModbusException("RS485 数据分块读取长度不匹配。");
            }
            values.CopyTo(result, offset);
            offset += chunk;
        }
        return result;
    }

    private void EnsureConnected()
    {
        if (!client.IsOpen)
        {
            throw new InvalidOperationException("以太网尚未连接。");
        }
    }

    private static uint JoinUInt32(ushort high, ushort low)
    {
        return ((uint)high << 16) | low;
    }
}
