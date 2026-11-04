using IndustrialFieldDataAcquisitionTerminal;

var directory = Path.Combine(Path.GetTempPath(), "daq-trend-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    using var recorder = new TrendRecorder(directory, maxPointsPerSeries: 2);
    var time = new DateTime(2026, 10, 17, 12, 0, 0);
    for (var index = 0; index < 3; index++)
    {
        recorder.AddSnapshot(new AcquisitionSnapshot
        {
            Timestamp = time.AddSeconds(index),
            Status = 0x0008,
            DigitalInputs = (ushort)(1 << index),
            AnalogVoltageMv = (ushort)(1000 + index),
            AnalogCurrentUa = 4000,
            Ad7606Raw = new short[] { (short)(-10 + index), 2, 3, 4, 5, 6, 7, 8 },
            Rs485Sensors = index == 1 ? new[]
            {
                new Rs485SensorTelemetry(0, 7, 4, 1, true, true, true, 0, 123, new ushort[] { 42 })
            } : index == 2 ? new[]
            {
                new Rs485SensorTelemetry(0, 7, 4, 1, true, true, false, 1, 124, new ushort[] { 42 })
            } : null
        });
    }

    Expect(recorder.SnapshotCount == 3, "snapshot count");
    Expect(recorder.GetPoints("AD:0").Select(point => point.Value).SequenceEqual(new[] { -9.0, -8.0 }), "bounded plot history");
    Expect(recorder.GetPoints("DI:0").Select(point => point.Value).SequenceEqual(new[] { 0.0, 0.0 }), "digital bit history");
    Expect(recorder.GetChannels("电流回环").Count == 2, "current loop has commanded and measured traces");
    Expect(recorder.GetChannels("电流回环").Single(channel => channel.Key == "LOOP:IN").Name == "CH2 实测",
        "current loop label identifies CH2");
    Expect(recorder.GetPoints("LOOP:SET").Last().Value == 4.0, "current loop command in mA");
    Expect(recorder.GetPoints("LOOP:IN").Count == 2, "current loop input is recorded");
    recorder.AddSnapshot(new AcquisitionSnapshot { Timestamp = time.AddSeconds(4), Status = 8,
        AnalogCurrentUa = 20000, Ad7606Raw = new short[] { 1000, 10158, 0, 0, 0, 0, 0, 0 } });
    Expect(Math.Abs(recorder.GetPoints("LOOP:IN").Last().Value - 20.0) < 0.1,
        "CH2 current conversion follows the 155 ohm effective gain");
    Expect(recorder.GetPoints("LOOP:SET").Last().Value == 20.0, "current command tracks latest output");
    var rs = recorder.GetPoints("RS:0:7:04:0");
    Expect(rs.Count == 2 && rs[0].Value == 42 && double.IsNaN(rs[1].Value), "failed RS485 sample is a gap");
    Expect(recorder.GetChannels("RS485").Count == 1, "RS485 channel discovered");

    var export = Path.Combine(directory, "export.csv");
    recorder.Export(export);
    var csv = File.ReadAllText(export);
    Expect(csv.Contains("2026-10-17 12:00:00.000,1,AD7606,CH1,-10,raw,OK", StringComparison.Ordinal), "oldest sample retained on disk");
    Expect(csv.Contains(",RS485,Sensor 1 R1 (addr 7),,raw,ERROR,7,04,0", StringComparison.Ordinal), "failed RS485 row exported");
    Expect(csv.Split('\n').Length > 50, "all channels exported");

    recorder.StartNewSession();
    recorder.AddSnapshot(new AcquisitionSnapshot
    {
        Timestamp = time.AddSeconds(4), Status = 8,
        DigitalInputsObservedAt = time.AddMilliseconds(10),
        Ad7606ObservedAt = time.AddMilliseconds(20),
        AnalogOutputsObservedAt = time.AddMilliseconds(30),
        Rs485ObservedAt = time.AddMilliseconds(40),
        Rs485Sensors = new[] { new Rs485SensorTelemetry(0, 7, 4, 1, true, true, true, 0, 123, new ushort[] { 42 }) }
    });
    for (var index = 0; index < 100; index++)
        recorder.AddSnapshot(new AcquisitionSnapshot { Timestamp = time.AddSeconds(index + 5), Status = 8 });
    recorder.Export(export);
    csv = File.ReadAllText(export);
    Expect(csv.Contains("2026-10-17 12:00:00.010,1,数字输入,IN1", StringComparison.Ordinal), "digital receive time is preserved");
    Expect(csv.Contains("2026-10-17 12:00:00.020,1,AD7606,CH1", StringComparison.Ordinal), "ADC receive time is preserved");
    Expect(csv.Contains("2026-10-17 12:00:00.040,1,RS485,Sensor 1 R1 (addr 7),42,raw,OK,7,04,0,HOST_RESPONSE_RECEIVED,123", StringComparison.Ordinal), "RS485 board tick remains separate from host time");
    Expect(csv.Contains(",101,AD7606,CH1,", StringComparison.Ordinal), "export waits for all accepted snapshots");

    recorder.StartNewSession();
    recorder.AddSnapshot(new AcquisitionSnapshot { Timestamp = time, Status = 8, SegmentId = 10 });
    recorder.AddSnapshot(new AcquisitionSnapshot { Timestamp = time.AddSeconds(30), Status = 8, SegmentId = 11 });
    Expect(recorder.GetPoints("AD:0").Select(p => p.SegmentId).SequenceEqual(new long[] { 10, 11 }), "paused acquisition keeps segment boundary");
    recorder.Export(export);
    csv = File.ReadAllText(export);
    Expect(csv.Contains("TimestampBasis,BoardPollTickMs,SegmentId") && csv.Contains("HOST_RESPONSE_RECEIVED,,11"), "CSV preserves acquisition segment");

    var previousPath = recorder.SessionPath;
    recorder.StartNewSession();
    Expect(recorder.SnapshotCount == 0 && recorder.GetPoints("AD:0").Count == 0, "new session clears plot");
    Expect(File.Exists(previousPath) && recorder.SessionPath != previousPath, "old session remains on disk");
    var repeated = new Rs485SensorTelemetry(0, 7, 4, 1, true, true, true, 0, 123, new ushort[] { 42 });
    for (var index = 0; index < 2; index++)
        recorder.AddSnapshot(new AcquisitionSnapshot { Timestamp = time.AddSeconds(index), Status = 8, Rs485Sensors = new[] { repeated } });
    Expect(recorder.GetPoints("RS:0:7:04:0").Count == 1, "unchanged RS485 sample is not plotted twice");
    recorder.AddSnapshot(new AcquisitionSnapshot { Timestamp = time.AddSeconds(3), Status = 8,
        Rs485Sensors = new[] { repeated with { SlaveAddress = 8, TimestampMs = 1 } } });
    Expect(recorder.GetPoints("RS:0:8:04:0").Count == 1 && recorder.GetChannels("RS485").Count == 2,
        "different slave addresses remain distinct channels");
    recorder.StartNewSession();
    recorder.AddSnapshot(new AcquisitionSnapshot { Timestamp = time, Status = 0x0002,
        Ad7606Raw = new short[] { -17, 0, 0, 0, 0, 0, 0, 0 } });
    var unconfirmed = recorder.GetPoints("AD:0").Single();
    Expect(unconfirmed.Value == -17 && !unconfirmed.IsValid, "unconfirmed AD raw value remains inspectable");
    var warningExport = Path.Combine(directory, "warning.csv");
    recorder.Export(warningExport);
    Expect(File.ReadAllText(warningExport).Contains(",AD7606,CH1,-17,raw,WARN", StringComparison.Ordinal),
        "CSV distinguishes unconfirmed raw data from a successful read");
    recorder.StartNewSession();
    var adScale = new EngineeringChannel("AD:0", "压力,入口", "MPa", 100, 500, 0, 1);
    var rsScale = new EngineeringChannel("RS:0:7:04:100:0", "温度\"点", "℃", 0, 1000, -20, 80);
    EngineeringChannel.Validate(new[] { adScale, rsScale });
    recorder.SetEngineeringChannels(new Dictionary<string, EngineeringChannel> { ["AD:0"] = adScale, ["RS:0:7:04:0"] = rsScale });
    recorder.AddSnapshot(new AcquisitionSnapshot { Timestamp = time, Status = 8, Ad7606Raw = new short[] { 300 },
        Rs485Sensors = new[] { new Rs485SensorTelemetry(0, 7, 4, 1, true, true, true, 0, 200, new ushort[] { 500 }) } });
    Expect(recorder.GetPoints("AD:0").Single().Value == 300, "ADC raw retained with conversion");
    Expect(Math.Abs(recorder.GetPoints("ENG:AD:0").Single().Value - 0.5) < 1e-9, "ADC engineering conversion");
    Expect(Math.Abs(recorder.GetPoints("ENG:RS:0:7:04:0").Single().Value - 30) < 1e-9, "RS485 engineering conversion");
    recorder.Export(export);
    csv = File.ReadAllText(export);
    Expect(csv.Contains(",AD7606,CH1,300,raw,OK") && csv.Contains(",AD7606,\"压力,入口\",0.5,MPa,OK"), "CSV contains raw and converted rows with escaped name");
    Expect(csv.Contains("\"温度\"\"点\",30,℃,OK"), "CSV escapes quotes in engineering names");
    recorder.AddSnapshot(new AcquisitionSnapshot { Timestamp = time.AddSeconds(1), Status = 0,
        Ad7606Raw = new short[] { 300 }, Rs485Sensors = new[] { new Rs485SensorTelemetry(0, 7, 4, 1, true, true, false, 1, 201, new ushort[] { 500 }) } });
    Expect(!recorder.GetPoints("ENG:AD:0").Last().IsValid, "unconfirmed ADC conversion marked invalid");
    Expect(double.IsNaN(recorder.GetPoints("ENG:RS:0:7:04:0").Last().Value), "failed RS485 conversion gap");
    try { EngineeringChannel.Validate(new[] { adScale with { RawHigh = 100 } }); throw new Exception("zero span accepted"); }
    catch (InvalidDataException) { }
    Console.WriteLine("Trend recorder, raw and engineering conversion, quality and CSV export passed.");
}
finally
{
    Directory.Delete(directory, recursive: true);
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

namespace IndustrialFieldDataAcquisitionTerminal
{
    internal static class Rs485ConfigurationProtocol
    {
        public const int RegistersPerItem = 16;
    }

    internal sealed record Rs485SensorTelemetry(int Index, byte SlaveAddress, byte Function, byte Quantity,
        bool Configured, bool Enabled, bool LastOk, uint ErrorCount, uint TimestampMs, ushort[] Values);
}
