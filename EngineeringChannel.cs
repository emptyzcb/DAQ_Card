using System.Globalization;
using System.Text.RegularExpressions;

namespace IndustrialFieldDataAcquisitionTerminal;

// Input endpoints are raw signed AD7606 codes or unsigned 16-bit RS485 register words.
internal sealed record EngineeringChannel(string Source, string Name, string Unit, double RawLow, double RawHigh,
    double ValueLow, double ValueHigh)
{
    public double Convert(double raw) => ValueLow + (raw - RawLow) * ((ValueHigh - ValueLow) / (RawHigh - RawLow));
    public static void Validate(IEnumerable<EngineeringChannel> items)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var item in items)
        {
            if (++count > 520 || item is null || string.IsNullOrWhiteSpace(item.Source) || !keys.Add(item.Source) ||
                !Regex.IsMatch(item.Source, @"^(AD:[0-7]|RS:(?:[0-9]|[12][0-9]|3[01]):(?:[1-9]|[1-9][0-9]|1[0-9]{2}|2[0-3][0-9]|24[0-7]):0[34]:[0-9]{1,5}:[0-9]{1,2})$") ||
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 80 || item.Name.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(item.Unit) || item.Unit.Length > 24 || item.Unit.Any(char.IsControl) ||
                !double.IsFinite(item.RawLow) || !double.IsFinite(item.RawHigh) || !double.IsFinite(item.ValueLow) ||
                !double.IsFinite(item.ValueHigh) || item.RawLow == item.RawHigh ||
                Math.Abs(item.RawLow) > 1e9 || Math.Abs(item.RawHigh) > 1e9 ||
                Math.Abs(item.ValueLow) > 1e12 || Math.Abs(item.ValueHigh) > 1e12 ||
                !double.IsFinite((item.ValueHigh - item.ValueLow) / (item.RawHigh - item.RawLow)))
                throw new InvalidDataException("工程量通道无效：请检查名称、单位、两个不同的原始点及有限的换算值。");
            if (item.Source.StartsWith("RS:", StringComparison.Ordinal))
            {
                var parts = item.Source.Split(':');
                if (!ushort.TryParse(parts[4], out var start) || !int.TryParse(parts[5], out var offset) || offset is < 0 or > 15 || (int)start + offset > ushort.MaxValue)
                    throw new InvalidDataException("RS485 寄存器范围无效。");
            }
            var limits = item.Source.StartsWith("AD:", StringComparison.Ordinal) ? new[] { -32768d, 32767d } : new[] { 0d, 65535d };
            if (limits.Any(raw => !double.IsFinite(item.Convert(raw)))) throw new InvalidDataException("换算系数超出数值范围。");
        }
    }
    public static string RsSource(int index, byte slave, byte function, ushort start, int offset) =>
        $"RS:{index}:{slave}:{function:X2}:{start}:{offset}";
    public static string Format(double value) => value.ToString("0.###", CultureInfo.CurrentCulture);
}
