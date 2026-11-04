using System.Buffers.Binary;

namespace IndustrialFieldDataAcquisitionTerminal;

/// <summary>编译产物：v2 字节码镜像（联合控制器 DSL）。</summary>
internal sealed record IoLogicCompiledImage(
    byte[] Bytes,
    ushort ProgramSize,
    uint Generation,
    uint BodyCrc32);

/// <summary>v2 字节码操作码（与固件 io_logic_engine.c 一致）。</summary>
internal static class IoLogicOpcode
{
    public const byte Nop = 0x00;
    public const byte Jmp = 0x01;
    public const byte TestDi = 0x10;
    public const byte TestDiRising = 0x11;
    public const byte TestDiFalling = 0x12;
    public const byte TestFreqClass = 0x13;
    public const byte TestFreqGt = 0x14;
    public const byte TestFreqLt = 0x15;
    public const byte TestExtModeEq = 0x16;
    public const byte TestCurModeEq = 0x17;
    public const byte TestExtNeqCur = 0x18;
    public const byte TestAiValid = 0x19;
    public const byte TestAiMv = 0x1A;
    public const byte TestCurrentUa = 0x1B;
    public const byte TestRs485Valid = 0x1C;
    public const byte TestRs485Value = 0x1D;
    public const byte DoSet = 0x20;
    public const byte DoPulse = 0x21;
    public const byte AoSetVoltage = 0x22;
    public const byte AoSetCurrent = 0x23;
    public const byte AoBreath = 0x24;
    public const byte AoBreathCurrent = 0x25;
    public const byte Delay = 0x30;
    public const byte SetMode = 0x31;
    public const byte RunCurrentMode = 0x32;
    public const byte ModeCall = 0x33;
    public const byte Ret = 0x34;
    public const byte SetModeExt = 0x35;
    public const byte SetFreqTh = 0x40;
    public const byte End = 0xFF;
}

internal static class IoLogicBinaryFormat
{
    public const uint Magic = 0x494F4346U;
    public const ushort FormatVersion = 2;
    public const ushort HeaderLength = 32;
    public const int ModeCount = 16;
    public const int ModeEntryTableLength = ModeCount * 2;
    public const int BodyOffset = HeaderLength + ModeEntryTableLength;
    public const int MaxImageLength = 8192;
    public const int MaxDataChunk = 128;
    public const int MaxProgramSize = MaxImageLength - BodyOffset;

    /// <summary>
    /// 构建 v2 字节码镜像。
    /// </summary>
    /// <param name="mainCode">主程序字节码。</param>
    /// <param name="modeCode">16 个模式回调字节码（可为 null，表示未定义的空入口）。</param>
    /// <param name="generation">程序代次。</param>
    public static IoLogicCompiledImage Create(
        ReadOnlySpan<byte> mainCode,
        IReadOnlyList<ReadOnlyMemory<byte>?> modeCode,
        uint generation = 1)
    {
        if (modeCode.Count != ModeCount)
        {
            throw new InvalidOperationException($"模式回调数量必须为 {ModeCount}。");
        }

        // 拼接：主程序 + 各模式回调（缺失的为 0 长度）。
        var total = mainCode.Length;
        var modeOffsets = new int[ModeCount];
        for (var index = 0; index < ModeCount; index++)
        {
            modeOffsets[index] = total;
            total += modeCode[index]?.Length ?? 0;
        }

        if (total > MaxProgramSize)
        {
            throw new InvalidOperationException($"程序字节码超过上限 {MaxProgramSize} 字节，请精简代码。");
        }

        var body = new byte[total];
        mainCode.CopyTo(body);
        for (var index = 0; index < ModeCount; index++)
        {
            if (modeCode[index] is { } code)
            {
                code.Span.CopyTo(body.AsSpan(modeOffsets[index]));
            }
        }

        var image = new byte[BodyOffset + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(0, 4), Magic);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(4, 2), FormatVersion);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(6, 2), HeaderLength);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(8, 4), generation);
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(12, 2), 10);           // scan_period_ms
        BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(14, 2), 0);            // reserved
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(16, 4), (uint)body.Length);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(20, 4), Crc32(body));
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(24, 4), 0);            // reserved
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(28, 4), Crc32(image.AsSpan(0, 28)));

        // 16 个模式回调入口偏移表（大端）。
        for (var index = 0; index < ModeCount; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(image.AsSpan(HeaderLength + index * 2, 2),
                                                  (ushort)modeOffsets[index]);
        }

        body.CopyTo(image, BodyOffset);
        return new IoLogicCompiledImage(image, (ushort)body.Length, generation,
                                        BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(20, 4)));
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
