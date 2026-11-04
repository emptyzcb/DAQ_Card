using System.Globalization;
using System.Text;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class SerialWaveformCapture
{
    private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder pending = new();
    private readonly List<byte> pendingBinary = new();
    private readonly List<Queue<TrendPoint>> channels = new();

    public int ChannelCount => channels.Count;
    public long SampleCount { get; private set; }

    public void Reset()
    {
        decoder.Reset();
        pending.Clear();
        pendingBinary.Clear();
        channels.Clear();
        SampleCount = 0;
    }

    public void Append(ReadOnlySpan<byte> bytes, DateTime timestamp)
    {
        if (bytes.IsEmpty) return;
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        var count = decoder.GetChars(bytes, chars, flush: false);
        for (var index = 0; index < count; index++)
        {
            var c = chars[index];
            if (c == '\n')
            {
                ParseLine(pending.ToString().TrimEnd('\r'), timestamp);
                pending.Clear();
            }
            else if (pending.Length < 4096) pending.Append(c);
            else pending.Clear();
        }
    }

    public IReadOnlyList<TrendPoint> GetPoints(int channel) =>
        channel >= 0 && channel < channels.Count ? channels[channel].ToArray() : Array.Empty<TrendPoint>();

    public void AppendJustFloat(ReadOnlySpan<byte> bytes, DateTime timestamp)
    {
        pendingBinary.AddRange(bytes.ToArray());
        if (pendingBinary.Count > 4096)
            pendingBinary.RemoveRange(0, pendingBinary.Count - 4096);
        while (pendingBinary.Count >= 8)
        {
            var tail = -1;
            for (var index = 0; index <= pendingBinary.Count - 4; index++)
                if (pendingBinary[index] == 0 && pendingBinary[index + 1] == 0 &&
                    pendingBinary[index + 2] == 0x80 && pendingBinary[index + 3] == 0x7F)
                {
                    tail = index;
                    break;
                }
            if (tail < 0) break;
            if (tail is >= 4 and <= 128 && tail % 4 == 0)
            {
                var values = new double[tail / 4];
                var valid = true;
                for (var index = 0; index < values.Length; index++)
                {
                    var value = BitConverter.ToSingle(pendingBinary.GetRange(index * 4, 4).ToArray());
                    if (!float.IsFinite(value)) { valid = false; break; }
                    values[index] = value;
                }
                if (valid) AddSample(values, timestamp);
            }
            pendingBinary.RemoveRange(0, tail + 4);
        }
    }

    private void ParseLine(string line, DateTime timestamp)
    {
        line = line.Trim();
        var separator = line.LastIndexOf(':');
        if (separator >= 0)
        {
            var prefix = line[..separator].Trim();
            if (prefix.Equals("image", StringComparison.OrdinalIgnoreCase) ||
                (prefix.Length > 0 && !prefix.Any(char.IsLetter))) return;
            line = line[(separator + 1)..].Trim();
        }
        var tokens = line.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 1 or > 32) return;
        var values = new double[tokens.Length];
        for (var index = 0; index < tokens.Length; index++)
            if (!double.TryParse(tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out values[index]) || !double.IsFinite(values[index])) return;
        AddSample(values, timestamp);
    }

    private void AddSample(double[] values, DateTime timestamp)
    {
        while (channels.Count < values.Length) channels.Add(new Queue<TrendPoint>());
        SampleCount++;
        for (var index = 0; index < channels.Count; index++)
        {
            var series = channels[index];
            series.Enqueue(index < values.Length
                ? new TrendPoint(timestamp, values[index])
                : new TrendPoint(timestamp, double.NaN, false));
            if (series.Count > 4096) series.Dequeue();
        }
    }
}
