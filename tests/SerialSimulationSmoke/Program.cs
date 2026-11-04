using IndustrialFieldDataAcquisitionTerminal;

static byte[] Request(byte address, byte function, ushort start, ushort quantity)
{
    var frame = new byte[] { address, function, (byte)(start >> 8), (byte)start,
        (byte)(quantity >> 8), (byte)quantity, 0, 0 };
    ModbusRtuSlaveSimulator.WriteCrc(frame);
    return frame;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var simulator = new ModbusRtuSlaveSimulator();
simulator.Configure(new[]
{
    new VirtualRs485Slave(1, 0, new ushort[] { 0x1234, 0x5678 }, new ushort[] { 0xABCD }),
    new VirtualRs485Slave(7, 10, new ushort[] { 0xCAFE }, new ushort[] { 0x00FF })
});
var request = Request(1, 3, 0, 2);
Check(simulator.Process(request.AsSpan(0, 3)).Count == 0, "Split frame responded too early.");
var reply = simulator.Process(request.AsSpan(3)).Single();
Check(reply.Response.AsSpan(0, 7).SequenceEqual(new byte[] { 1, 3, 4, 0x12, 0x34, 0x56, 0x78 }),
    "FC03 register response mismatch.");
Check(ModbusRtuSlaveSimulator.HasValidCrc(reply.Response), "FC03 response CRC failed.");

var request2 = Request(7, 4, 10, 1);
var request3 = Request(1, 3, 20, 1);
var combined = request2.Concat(request3).ToArray();
var replies = simulator.Process(combined);
Check(replies.Count == 2 && replies[0].Response.AsSpan(0, 5)
        .SequenceEqual(new byte[] { 7, 4, 2, 0, 0xFF }), "Second address/FC04 mismatch.");
Check(replies[1].Response.AsSpan(0, 3)
        .SequenceEqual(new byte[] { 1, 0x83, 2 }), "Out-of-range exception mismatch.");
Check(replies.All(item => ModbusRtuSlaveSimulator.HasValidCrc(item.Response)),
    "Response CRC failed.");

var broken = Request(1, 3, 0, 1);
broken[^1] ^= 0xFF;
Check(simulator.Process(broken).Count == 0, "Bad CRC should be ignored.");
Check(simulator.Process(Request(4, 3, 0, 1)).Count == 0, "Unknown address should be ignored.");
Check(simulator.Process(Request(1, 3, 0, 1)).Count == 1, "Parser failed to resynchronize.");

simulator.Configure(new[]
{
    new VirtualRs485Slave(1, 0, new ushort[] { 1 }, new ushort[] { 1 }, VirtualSlaveFault.Timeout),
    new VirtualRs485Slave(2, 0, new ushort[] { 2 }, new ushort[] { 2 }, VirtualSlaveFault.BadCrc),
    new VirtualRs485Slave(3, 0, new ushort[] { 3 }, new ushort[] { 3 }, VirtualSlaveFault.IllegalAddress),
    new VirtualRs485Slave(4, 0, new ushort[] { 4 }, new ushort[] { 4 }, VirtualSlaveFault.DeviceFailure),
    new VirtualRs485Slave(5, 0, new ushort[] { 5 }, new ushort[] { 5 })
});
var faults = simulator.Process(Enumerable.Range(1, 5)
    .SelectMany(address => Request((byte)address, 3, 0, 1)).ToArray());
Check(faults.Count == 5 && faults[0].Response.Length == 0, "Timeout must not transmit a response.");
Check(!ModbusRtuSlaveSimulator.HasValidCrc(faults[1].Response), "CRC fault must corrupt the response.");
Check(faults[2].Response[1] == 0x83 && faults[2].Response[2] == 2 && ModbusRtuSlaveSimulator.HasValidCrc(faults[2].Response), "Exception 02 mismatch.");
Check(faults[3].Response[1] == 0x83 && faults[3].Response[2] == 4 && ModbusRtuSlaveSimulator.HasValidCrc(faults[3].Response), "Exception 04 mismatch.");
Check(ModbusRtuSlaveSimulator.HasValidCrc(faults[4].Response) && faults[4].Response[4] == 5, "Other slaves must still respond normally.");

var raw = new RawFrameResponder();
raw.Configure(new[] { new RawResponseRule(new byte[] { 0xAA, 0x55, 1 },
    new byte[] { 0xAA, 0x55, 0 }) });
Check(raw.Process(new byte[] { 0xFF, 0xAA }).Count == 0, "Raw parser responded too early.");
Check(raw.Process(new byte[] { 0x55, 1 }).Single().Response
    .SequenceEqual(new byte[] { 0xAA, 0x55, 0 }), "Raw response mismatch.");
var rejectedAmbiguity = false;
try
{
    raw.Configure(new[]
    {
        new RawResponseRule(new byte[] { 1 }, new byte[] { 2 }),
        new RawResponseRule(new byte[] { 1, 3 }, new byte[] { 4 })
    });
}
catch (ArgumentException) { rejectedAmbiguity = true; }
Check(rejectedAmbiguity, "Ambiguous raw-frame prefixes must be rejected.");
Console.WriteLine("Serial simulation smoke tests passed.");
