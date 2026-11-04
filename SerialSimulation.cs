namespace IndustrialFieldDataAcquisitionTerminal;

internal enum VirtualSlaveFault { None, Timeout, BadCrc, IllegalAddress, DeviceFailure }

internal sealed record VirtualRs485Slave(
    byte Address, ushort StartRegister, ushort[] HoldingRegisters, ushort[] InputRegisters,
    VirtualSlaveFault Fault = VirtualSlaveFault.None);

internal sealed record RawResponseRule(byte[] Request, byte[] Response);

internal sealed record SimulatedReply(byte Address, byte Function, byte[] Request, byte[] Response);

internal sealed class ModbusRtuSlaveSimulator
{
    private readonly List<byte> pending = new();
    private IReadOnlyDictionary<byte, VirtualRs485Slave> slaves = new Dictionary<byte, VirtualRs485Slave>();

    public void Configure(IEnumerable<VirtualRs485Slave> definitions)
    {
        var items = definitions.ToArray();
        if (items.Any(item => item.Address is < 1 or > 247))
            throw new ArgumentException("从站地址必须在 1–247 之间。");
        if (items.Select(item => item.Address).Distinct().Count() != items.Length)
            throw new ArgumentException("虚拟从站地址不能重复。");
        slaves = items.ToDictionary(item => item.Address);
        pending.Clear();
    }

    public IReadOnlyList<SimulatedReply> Process(ReadOnlySpan<byte> bytes)
    {
        pending.AddRange(bytes.ToArray());
        if (pending.Count > 512) pending.RemoveRange(0, pending.Count - 512);
        var replies = new List<SimulatedReply>();
        while (pending.Count >= 8)
        {
            var request = pending.Take(8).ToArray();
            if (!HasValidCrc(request))
            {
                pending.RemoveAt(0);
                continue;
            }
            pending.RemoveRange(0, 8);
            if (!slaves.TryGetValue(request[0], out var slave)) continue;

            var function = request[1];
            if (slave.Fault == VirtualSlaveFault.Timeout)
            {
                replies.Add(new SimulatedReply(request[0], function, request, Array.Empty<byte>()));
                continue;
            }
            var start = (request[2] << 8) | request[3];
            var quantity = (request[4] << 8) | request[5];
            byte[] response;
            if (slave.Fault is VirtualSlaveFault.IllegalAddress or VirtualSlaveFault.DeviceFailure)
            {
                response = ExceptionResponse(request[0], function,
                    slave.Fault == VirtualSlaveFault.IllegalAddress ? (byte)2 : (byte)4);
            }
            else if (function is not (3 or 4))
            {
                response = ExceptionResponse(request[0], function, 1);
            }
            else if (quantity is < 1 or > 125)
            {
                response = ExceptionResponse(request[0], function, 3);
            }
            else
            {
                var values = function == 3 ? slave.HoldingRegisters : slave.InputRegisters;
                var offset = start - slave.StartRegister;
                if (offset < 0 || offset + quantity > values.Length)
                {
                    response = ExceptionResponse(request[0], function, 2);
                }
                else
                {
                    response = new byte[3 + quantity * 2 + 2];
                    response[0] = request[0];
                    response[1] = function;
                    response[2] = (byte)(quantity * 2);
                    for (var index = 0; index < quantity; index++)
                    {
                        var value = values[offset + index];
                        response[3 + index * 2] = (byte)(value >> 8);
                        response[4 + index * 2] = (byte)value;
                    }
                    WriteCrc(response);
                }
            }
            if (slave.Fault == VirtualSlaveFault.BadCrc) response[^1] ^= 0xFF;
            replies.Add(new SimulatedReply(request[0], function, request, response));
        }
        return replies;
    }

    private static byte[] ExceptionResponse(byte address, byte function, byte code)
    {
        var response = new byte[] { address, (byte)(function | 0x80), code, 0, 0 };
        WriteCrc(response);
        return response;
    }

    public static ushort Crc(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)(((crc & 1) != 0) ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return crc;
    }

    public static bool HasValidCrc(ReadOnlySpan<byte> frame) =>
        frame.Length >= 4 && Crc(frame[..^2]) == (frame[^2] | frame[^1] << 8);

    public static void WriteCrc(Span<byte> frame)
    {
        var crc = Crc(frame[..^2]);
        frame[^2] = (byte)crc;
        frame[^1] = (byte)(crc >> 8);
    }
}

internal sealed class RawFrameResponder
{
    private readonly List<byte> pending = new();
    private RawResponseRule[] rules = Array.Empty<RawResponseRule>();

    public void Configure(IEnumerable<RawResponseRule> definitions)
    {
        rules = definitions.Select(rule => new RawResponseRule((byte[])rule.Request.Clone(),
            (byte[])rule.Response.Clone())).ToArray();
        if (rules.Any(rule => rule.Request.Length == 0 || rule.Response.Length == 0))
            throw new ArgumentException("请求帧和响应帧都不能为空。");
        for (var first = 0; first < rules.Length; first++)
            for (var second = first + 1; second < rules.Length; second++)
                if (rules[first].Request.AsSpan().StartsWith(rules[second].Request) ||
                    rules[second].Request.AsSpan().StartsWith(rules[first].Request))
                    throw new ArgumentException("自定义请求帧不能重复，也不能互为前缀。");
        pending.Clear();
    }

    public IReadOnlyList<SimulatedReply> Process(ReadOnlySpan<byte> bytes)
    {
        pending.AddRange(bytes.ToArray());
        if (pending.Count > 2048) pending.RemoveRange(0, pending.Count - 2048);
        var replies = new List<SimulatedReply>();
        while (pending.Count > 0)
        {
            var matches = rules.Where(rule => pending.Count <= rule.Request.Length
                ? pending.SequenceEqual(rule.Request.Take(pending.Count))
                : pending.Take(rule.Request.Length).SequenceEqual(rule.Request)).ToArray();
            if (matches.Length == 0)
            {
                pending.RemoveAt(0);
                continue;
            }
            var complete = matches.FirstOrDefault(rule => pending.Count >= rule.Request.Length);
            if (complete is null) break;
            pending.RemoveRange(0, complete.Request.Length);
            replies.Add(new SimulatedReply(0, 0, complete.Request, complete.Response));
        }
        return replies;
    }
}
