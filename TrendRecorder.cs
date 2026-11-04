using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace IndustrialFieldDataAcquisitionTerminal;

internal readonly record struct TrendPoint(DateTime Timestamp, double Value, bool IsValid = true, long SegmentId = 0);
internal sealed record TrendChannel(string Key, string Category, string Name, string Unit);
internal readonly record struct TrendCsvRow(DateTime Timestamp, long Sample, TrendChannel Channel, double Value,
    bool IsValid, byte? Slave, byte? Function, int? Offset, uint? BoardTick, long Segment);

// The UI never performs disk writes. Export is ordered after all previously accepted rows.
internal sealed class AsyncTrendWriter : IDisposable
{
    private sealed record Command(IReadOnlyList<TrendCsvRow>? Rows, string? Destination, TaskCompletionSource? Completion);
    private readonly Channel<Command> queue = Channel.CreateBounded<Command>(new BoundedChannelOptions(2048)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait
    });
    private readonly string path;
    private readonly Task worker;
    private Exception? failure;
    public event Action<Exception>? Faulted;

    public AsyncTrendWriter(string path)
    {
        this.path = path;
        // Create immediately so invalid locations are reported during session creation.
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        worker = Task.Run(() => RunAsync(stream));
    }

    public void Enqueue(IReadOnlyList<TrendCsvRow> rows)
    {
        if (Volatile.Read(ref failure) is { } error) throw new IOException("趋势记录写入失败", error);
        if (!queue.Writer.TryWrite(new Command(rows, null, null)))
            throw new IOException("趋势记录缓冲区已满或已关闭，本次数据未写入；请检查磁盘后新建记录。");
    }

    public async Task ExportAsync(string destination)
    {
        if (Volatile.Read(ref failure) is { } error) throw new IOException("趋势记录写入失败", error);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new Command(null, destination, completed));
        await completed.Task;
    }

    private async Task RunAsync(FileStream stream)
    {
        try
        {
            await using var output = new StreamWriter(stream, new UTF8Encoding(true));
            await output.WriteLineAsync("Timestamp,Sample,Category,Channel,Value,Unit,Quality,Slave,Function,Offset,TimestampBasis,BoardPollTickMs,SegmentId");
            await foreach (var command in queue.Reader.ReadAllAsync())
            {
                if (command.Rows is not null)
                {
                    await output.WriteAsync(FormatRows(command.Rows));
                    await output.FlushAsync();
                }
                else if (command.Completion is not null)
                {
                    try
                    {
                        await output.FlushAsync();
                        if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(command.Destination!), StringComparison.OrdinalIgnoreCase))
                            File.Copy(path, command.Destination!, overwrite: true);
                        command.Completion.TrySetResult();
                    }
                    catch (Exception ex) { command.Completion.TrySetException(ex); }
                }
            }
        }
        catch (Exception ex)
        {
            Volatile.Write(ref failure, ex);
            queue.Writer.TryComplete(ex);
            while (queue.Reader.TryRead(out var pending)) pending.Completion?.TrySetException(ex);
            try { Faulted?.Invoke(ex); } catch { /* A UI shutdown must not hide the file error. */ }
        }
    }

    private static string FormatRows(IReadOnlyList<TrendCsvRow> rows)
    {
        var text = new StringBuilder(rows.Count * 90);
        foreach (var row in rows)
        {
            text.Append(row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Sample).Append(',').Append(row.Channel.Category).Append(',')
                .Append(CsvField(row.Channel.Name)).Append(',')
                .Append(double.IsNaN(row.Value) ? "" : row.Value.ToString("G17", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvField(row.Channel.Unit)).Append(',').Append(double.IsNaN(row.Value) ? "ERROR" : row.IsValid ? "OK" : "WARN").Append(',')
                .Append(row.Slave?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Function?.ToString("X2", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Offset?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append("HOST_RESPONSE_RECEIVED,")
                .Append(row.BoardTick?.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Segment.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }
        return text.ToString();
    }

    private static string CsvField(string value) => value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    public void Dispose()
    {
        queue.Writer.TryComplete();
        worker.GetAwaiter().GetResult();
    }
}

internal sealed class TrendRecorder : IDisposable
{
    private readonly string directory;
    private readonly int maxPointsPerSeries;
    private readonly Dictionary<string, Queue<TrendPoint>> points = new(StringComparer.Ordinal);
    private readonly List<TrendChannel> channels = new();
    private readonly Dictionary<string, TrendChannel> channelLookup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (uint TimestampMs, uint ErrorCount, bool LastOk)> rs485Markers = new(StringComparer.Ordinal);
    private AsyncTrendWriter? writer;
    private long currentSegmentId;
    private IReadOnlyDictionary<string, EngineeringChannel> engineering = new Dictionary<string, EngineeringChannel>();
    public event Action<Exception>? RecordingError;

    public TrendRecorder(string directory, int maxPointsPerSeries = 4096)
    {
        if (maxPointsPerSeries < 1) throw new ArgumentOutOfRangeException(nameof(maxPointsPerSeries));
        this.directory = directory;
        this.maxPointsPerSeries = maxPointsPerSeries;
        Directory.CreateDirectory(directory);
        StartNewSession();
    }

    public void SetEngineeringChannels(IReadOnlyDictionary<string, EngineeringChannel> channels) => engineering = channels;

    public string SessionPath { get; private set; } = string.Empty;
    public long SnapshotCount { get; private set; }
    public IReadOnlyList<TrendChannel> GetChannels(string category) =>
        channels.Where(channel => channel.Category == category).ToArray();
    public IReadOnlyList<TrendPoint> GetPoints(string key) =>
        points.TryGetValue(key, out var series) ? series.ToArray() : Array.Empty<TrendPoint>();

    public void StartNewSession()
    {
        writer?.Dispose();
        points.Clear();
        channels.Clear();
        channelLookup.Clear();
        rs485Markers.Clear();
        SnapshotCount = 0;
        for (var index = 0; index < 8; index++)
        {
            Register($"AD:{index}", "AD7606", $"CH{index + 1}", "raw");
            Register($"DI:{index}", "数字输入", $"IN{index + 1}", "0/1");
        }
        Register("AO:V", "模拟输出", "电压输出 U1", "V");
        Register("AO:I", "模拟输出", "电流输出 I1", "mA");
        Register("LOOP:SET", "电流回环", "I1 设定", "mA");
        Register("LOOP:IN", "电流回环", "CH2 实测", "mA");
        SessionPath = Path.Combine(directory, $"session_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.csv");
        var sessionWriter = new AsyncTrendWriter(SessionPath);
        writer = sessionWriter;
        sessionWriter.Faulted += ex => { if (ReferenceEquals(writer, sessionWriter)) RecordingError?.Invoke(ex); };
    }

    public void AddSnapshot(AcquisitionSnapshot snapshot)
    {
        if (writer is null) throw new ObjectDisposedException(nameof(TrendRecorder));
        SnapshotCount++;
        currentSegmentId = snapshot.SegmentId;
        var rows = new List<TrendCsvRow>(24);
        var adcValid = (snapshot.Status & 0x0008) != 0;
        for (var index = 0; index < 8; index++)
        {
            var raw = index < snapshot.Ad7606Raw.Length ? snapshot.Ad7606Raw[index] : 0;
            Add(rows, $"AD:{index}", snapshot.Ad7606ObservedAt ?? snapshot.Timestamp,
                index < snapshot.Ad7606Raw.Length ? raw : double.NaN,
                isValid: adcValid && index < snapshot.Ad7606Raw.Length);
            AddEngineering(rows, $"AD:{index}", "AD7606", snapshot.Ad7606ObservedAt ?? snapshot.Timestamp,
                index < snapshot.Ad7606Raw.Length ? raw : double.NaN, adcValid && index < snapshot.Ad7606Raw.Length);
            Add(rows, $"DI:{index}", snapshot.DigitalInputsObservedAt ?? snapshot.Timestamp, (snapshot.DigitalInputs >> index) & 1);
        }
        Add(rows, "AO:V", snapshot.AnalogOutputsObservedAt ?? snapshot.Timestamp, snapshot.AnalogVoltageMv / 1000.0);
        Add(rows, "AO:I", snapshot.AnalogOutputsObservedAt ?? snapshot.Timestamp, snapshot.AnalogCurrentUa / 1000.0);
        Add(rows, "LOOP:SET", snapshot.AnalogOutputsObservedAt ?? snapshot.Timestamp, snapshot.AnalogCurrentUa / 1000.0);
        var currentRaw = snapshot.Ad7606Raw.Length > 1 ? snapshot.Ad7606Raw[1] : 0;
        Add(rows, "LOOP:IN", snapshot.Ad7606ObservedAt ?? snapshot.Timestamp,
            snapshot.Ad7606Raw.Length > 1 ? Math.Max(0, currentRaw * 10000.0 / (32768.0 * 155.0)) : double.NaN,
            isValid: adcValid && snapshot.Ad7606Raw.Length > 1);

        if (snapshot.Rs485Sensors is not null)
        {
            foreach (var sensor in snapshot.Rs485Sensors)
            {
                var sensorKey = $"RS:{sensor.Index}:{sensor.SlaveAddress}:{sensor.Function:X2}";
                var marker = (sensor.TimestampMs, sensor.ErrorCount, sensor.LastOk);
                if (rs485Markers.TryGetValue(sensorKey, out var previous) && previous == marker) continue;
                rs485Markers[sensorKey] = marker;
                var valid = sensor.Configured && sensor.Enabled && sensor.LastOk;
                var quantity = Math.Min(sensor.Quantity, (byte)Rs485ConfigurationProtocol.RegistersPerItem);
                for (var offset = 0; offset < quantity; offset++)
                {
                    var key = $"{sensorKey}:{offset}";
                    Register(key, "RS485", $"Sensor {sensor.Index + 1} R{offset + 1} (addr {sensor.SlaveAddress})", "raw");
                    var value = valid && offset < sensor.Values.Length ? sensor.Values[offset] : double.NaN;
                    Add(rows, key, snapshot.Rs485ObservedAt ?? snapshot.Timestamp, value, !double.IsNaN(value),
                        sensor.SlaveAddress, sensor.Function, offset, sensor.TimestampMs);
                    AddEngineering(rows, key, "RS485", snapshot.Rs485ObservedAt ?? snapshot.Timestamp, value,
                        !double.IsNaN(value), sensor.SlaveAddress, sensor.Function, offset, sensor.TimestampMs);
                }
            }
        }

        try { writer.Enqueue(rows); }
        catch { SnapshotCount--; throw; }
    }

    public void Export(string destinationPath)
    {
        ExportAsync(destinationPath).GetAwaiter().GetResult();
    }

    public Task ExportAsync(string destinationPath) =>
        (writer ?? throw new ObjectDisposedException(nameof(TrendRecorder))).ExportAsync(destinationPath);

    private void AddEngineering(List<TrendCsvRow> rows, string source, string category, DateTime timestamp, double raw, bool valid,
        byte? slave = null, byte? function = null, int? offset = null, uint? tick = null)
    {
        if (!engineering.TryGetValue(source, out var config)) return;
        var key = "ENG:" + source;
        Register(key, category, config.Name, config.Unit);
        var value = double.IsFinite(raw) ? config.Convert(raw) : double.NaN;
        if (!double.IsFinite(value)) value = double.NaN;
        Add(rows, key, timestamp, value, valid && double.IsFinite(value), slave, function, offset, tick);
    }

    private void Register(string key, string category, string name, string unit)
    {
        if (points.ContainsKey(key)) return;
        points.Add(key, new Queue<TrendPoint>());
        var channel = new TrendChannel(key, category, name, unit);
        channels.Add(channel);
        channelLookup.Add(key, channel);
    }

    private void Add(List<TrendCsvRow> rows, string key, DateTime timestamp, double value, bool isValid = true,
        byte? slave = null, byte? function = null, int? offset = null, uint? boardEventTickMs = null)
    {
        var series = points[key];
        series.Enqueue(new TrendPoint(timestamp, value, isValid, currentSegmentId));
        while (series.Count > maxPointsPerSeries) series.Dequeue();
        rows.Add(new TrendCsvRow(timestamp, SnapshotCount, channelLookup[key], value, isValid,
            slave, function, offset, boardEventTickMs, currentSegmentId));
    }

    public void Dispose()
    {
        writer?.Dispose();
        writer = null;
    }
}
