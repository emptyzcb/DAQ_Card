using System.Buffers.Binary;
using System.Globalization;

namespace IndustrialFieldDataAcquisitionTerminal;

internal static class DataConversion
{
    public static byte[] ParseHex(string text)
    {
        var value = string.Concat(text.Where(c => !char.IsWhiteSpace(c) && c != ',' && c != '-'));
        if (value.Length == 0 || value.Length % 2 != 0 || value.Length > 8192 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("请输入成对的十六进制字节，例如 12 34 AB CD（最多 4096 字节）。");
        return Convert.FromHexString(value);
    }
    public static byte[] Reorder(ReadOnlySpan<byte> source, bool littleEndian, bool swapWords)
    {
        if (source.Length is not (2 or 4 or 8)) throw new ArgumentException("数据类型只支持 2、4 或 8 字节。");
        var bytes = source.ToArray();
        if (swapWords && bytes.Length > 2)
            for (int i = 0, j = bytes.Length - 2; i < j; i += 2, j -= 2)
            { (bytes[i], bytes[j]) = (bytes[j], bytes[i]); (bytes[i + 1], bytes[j + 1]) = (bytes[j + 1], bytes[i + 1]); }
        if (littleEndian) Array.Reverse(bytes);
        return bytes;
    }
    public static string Decode(byte[] source, string type, bool littleEndian, bool swapWords)
    {
        var expected = Size(type);
        if (source.Length != expected) throw new ArgumentException($"{type} 需要 {expected} 字节，当前为 {source.Length} 字节。");
        var bytes = Reorder(source, littleEndian, swapWords);
        ulong raw = expected switch { 2 => BinaryPrimitives.ReadUInt16BigEndian(bytes), 4 => BinaryPrimitives.ReadUInt32BigEndian(bytes), _ => BinaryPrimitives.ReadUInt64BigEndian(bytes) };
        return type switch
        {
            "UInt16" => ((ushort)raw).ToString(CultureInfo.InvariantCulture), "Int16" => unchecked((short)raw).ToString(CultureInfo.InvariantCulture),
            "UInt32" => ((uint)raw).ToString(CultureInfo.InvariantCulture), "Int32" => unchecked((int)raw).ToString(CultureInfo.InvariantCulture),
            "Float32" => BitConverter.Int32BitsToSingle(unchecked((int)raw)).ToString("R", CultureInfo.InvariantCulture),
            "UInt64" => raw.ToString(CultureInfo.InvariantCulture), "Int64" => unchecked((long)raw).ToString(CultureInfo.InvariantCulture),
            "Float64" => BitConverter.Int64BitsToDouble(unchecked((long)raw)).ToString("R", CultureInfo.InvariantCulture),
            _ => throw new ArgumentException("未知数据类型。")
        };
    }
    public static byte[] Encode(string value, string type, bool littleEndian, bool swapWords)
    {
        var size = Size(type);
        ulong raw = type switch
        {
            "UInt16" => ushort.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            "Int16" => unchecked((ushort)short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            "UInt32" => uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            "Int32" => unchecked((uint)int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            "Float32" => unchecked((uint)BitConverter.SingleToInt32Bits(float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))),
            "UInt64" => ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            "Int64" => unchecked((ulong)long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)),
            "Float64" => unchecked((ulong)BitConverter.DoubleToInt64Bits(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))),
            _ => throw new ArgumentException("未知数据类型。")
        };
        var bytes = new byte[size];
        if (size == 2) BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)raw);
        else if (size == 4) BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)raw);
        else BinaryPrimitives.WriteUInt64BigEndian(bytes, raw);
        // Reverse transformations in inverse order; word reversal and byte reversal commute for 2-byte words.
        return Reorder(bytes, littleEndian, swapWords);
    }
    private static int Size(string type) => type switch
    {
        "UInt16" or "Int16" => 2,
        "UInt32" or "Int32" or "Float32" => 4,
        "UInt64" or "Int64" or "Float64" => 8,
        _ => throw new ArgumentException("未知数据类型。")
    };
}
