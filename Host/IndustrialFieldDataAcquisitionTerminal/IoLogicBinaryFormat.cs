using System.Buffers.Binary;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed record IoLogicCompiledImage(
    byte[] Bytes,
    ushort RuleCount,
    uint Generation,
    uint BodyCrc32);

internal sealed record IoLogicCompiledRule(
    byte ConditionType,
    byte InputOperator,
    ushort InputMask,
    ushort InputExpected,
    byte EdgeType,
    byte Action,
    ushort OutputMask,
    ushort DelayMs,
    ushort PulseMs,
    byte Priority,
    bool Enabled);

internal static class IoLogicBinaryFormat
{
    public const uint Magic = 0x494F4346U;
    public const ushort FormatVersion = 1;
    public const ushort HeaderLength = 32;
    public const int RuleLength = 20;
    public const int MaxImageLength = 4096;
    public const int MaxRuleCount = 32;
    public const int MaxDataChunk = 128;

    public static IoLogicCompiledImage Create(IReadOnlyList<IoLogicCompiledRule> rules, uint generation = 1)
    {
        if (rules.Count > MaxRuleCount)
        {
            throw new InvalidOperationException($"规则数量超过上限 {MaxRuleCount}。 ");
        }

        var body = new byte[rules.Count * RuleLength];
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var offset = index * RuleLength;
            body[offset] = rule.ConditionType;
            body[offset + 1] = rule.InputOperator;
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(offset + 2, 2), rule.InputMask);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(offset + 4, 2), rule.InputExpected);
            body[offset + 6] = rule.EdgeType;
            body[offset + 7] = rule.Action;
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(offset + 8, 2), rule.OutputMask);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(offset + 10, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(offset + 12, 2), rule.DelayMs);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(offset + 14, 2), rule.PulseMs);
            body[offset + 16] = rule.Priority;
            body[offset + 17] = rule.Enabled ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(offset + 18, 2), 0);
        }

        var image = new byte[HeaderLength + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(4, 2), FormatVersion);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(6, 2), HeaderLength);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(8, 4), generation);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(12, 4), 0x00000003U);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(16, 2), 10);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(18, 2), (ushort)rules.Count);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(20, 4), (uint)body.Length);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(24, 4), Crc32(body));
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(28, 4), Crc32(image.AsSpan(0, 28)));
        body.CopyTo(image, HeaderLength);

        return new IoLogicCompiledImage(image, (ushort)rules.Count, generation, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(24, 4)));
    }

    public static byte[] WithGeneration(ReadOnlySpan<byte> image, uint generation)
    {
        var copy = image.ToArray();
        if (copy.Length < HeaderLength)
        {
            throw new InvalidDataException("IOCF 镜像头长度不足。");
        }

        BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(8, 4), generation);
        BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(28, 4), Crc32(copy.AsSpan(0, 28)));
        return copy;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFU;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1U) != 0U ? (crc >> 1) ^ 0xEDB88320U : crc >> 1;
            }
        }

        return ~crc;
    }
}
