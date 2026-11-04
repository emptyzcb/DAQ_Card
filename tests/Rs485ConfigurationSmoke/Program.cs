using IndustrialFieldDataAcquisitionTerminal;

var client = new FakeRegisterClient();
var protocol = new Rs485ConfigurationProtocol(client);
var entries = new[]
{
    new Rs485SensorConfig("Temperature", true, 1, 0x04, 0x0010, 2, 500),
    new Rs485SensorConfig("Pressure", true, 7, 0x03, 0x0100, 4, 1000)
};

protocol.Download(entries);
ExpectWrite(client, 0x0400, 1);
ExpectWrite(client, 0x0401, 0x0401);
ExpectWrite(client, 0x0402, 0x0010);
ExpectWrite(client, 0x0403, 2);
ExpectWrite(client, 0x0404, 500);
ExpectWrite(client, 0x0406, 1);
ExpectWrite(client, 0x0407, 0x0307);
ExpectWrite(client, 0x0284, 2);
ExpectWrite(client, 0x0285, 0xA55A);

client.Set(0x0300, 1, 4, 2, 7, 0, 0, 0x1234, 0x5678);
client.Set(0x0080, 0x1111, 0x2222);
var telemetry = protocol.ReadTelemetry();
if (telemetry.Count != 2 || !telemetry[0].LastOk || telemetry[0].Values[1] != 0x2222)
{
    throw new InvalidOperationException("RS485 telemetry decoding failed.");
}

try
{
    protocol.Download(new[] { entries[0] with { Quantity = 17 } });
    throw new InvalidOperationException("Invalid register quantity was accepted.");
}
catch (ArgumentException)
{
}

Console.WriteLine("RS485 configuration protocol smoke test passed.");

static void ExpectWrite(FakeRegisterClient client, ushort address, ushort value)
{
    if (!client.Writes.Contains((address, value)))
    {
        throw new InvalidOperationException($"Missing write 0x{address:X4}=0x{value:X4}.");
    }
}

internal sealed class FakeRegisterClient : IModbusRegisterClient
{
    private readonly Dictionary<ushort, ushort> registers = new();
    public List<(ushort Address, ushort Value)> Writes { get; } = new();
    public bool IsOpen => true;

    public ushort[] ReadHoldingRegisters(ushort startAddress, ushort quantity)
    {
        var values = new ushort[quantity];
        for (var index = 0; index < quantity; index++)
        {
            registers.TryGetValue((ushort)(startAddress + index), out values[index]);
        }
        return values;
    }

    public void WriteSingleRegister(ushort address, ushort value)
    {
        Writes.Add((address, value));
        registers[address] = value;
        if (address == 0x0284)
        {
            registers[0x0280] = value;
            registers[0x0281] = 32;
        }
    }

    public void Set(ushort startAddress, params ushort[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            registers[(ushort)(startAddress + index)] = values[index];
        }
    }
}
