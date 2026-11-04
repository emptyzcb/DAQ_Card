using System.Text;
using IndustrialFieldDataAcquisitionTerminal;

var capture = new SerialWaveformCapture();
var first = DateTime.UtcNow;
capture.Append(Encoding.UTF8.GetBytes("1.2,2."), first);
if (capture.SampleCount != 0) throw new Exception("Partial line was parsed.");
capture.Append(Encoding.UTF8.GetBytes("5\r\n-3 4\nno data\n"), first);
if (capture.SampleCount != 2 || capture.ChannelCount != 2 ||
    capture.GetPoints(0).Select(point => point.Value).SequenceEqual(new[] { 1.2, -3.0 }) == false ||
    capture.GetPoints(1).Select(point => point.Value).SequenceEqual(new[] { 2.5, 4.0 }) == false)
    throw new Exception("Split/multi-channel numeric parsing failed.");
capture.Reset();
if (capture.SampleCount != 0 || capture.GetPoints(0).Count != 0)
    throw new Exception("Clear failed.");
capture.Append(Encoding.UTF8.GetBytes("2.64\nchannels: 1.0, 2.0, 3.0\nboot ready\nimage:1,2\n"), first);
if (capture.SampleCount != 2 || capture.ChannelCount != 3 ||
    capture.GetPoints(0).Last().Value != 1.0 || capture.GetPoints(2).Last().Value != 3.0)
    throw new Exception("FireWater-style numeric lines were not separated from ordinary logs.");
capture.Reset();
var twelve = string.Join(',', Enumerable.Range(1, 12));
capture.Append(Encoding.UTF8.GetBytes(twelve + "\n5\n"), first);
if (capture.ChannelCount != 12 || capture.SampleCount != 2 ||
    capture.GetPoints(11).Last().IsValid || !double.IsNaN(capture.GetPoints(11).Last().Value))
    throw new Exception("Serial channels must be dynamic and missing values must create gaps.");
capture.Reset();
var binary = new byte[12];
BitConverter.GetBytes(1.25f).CopyTo(binary, 0);
BitConverter.GetBytes(-2.5f).CopyTo(binary, 4);
new byte[] { 0, 0, 0x80, 0x7F }.CopyTo(binary, 8);
capture.AppendJustFloat(binary.AsSpan(0, 5), first);
if (capture.SampleCount != 0) throw new Exception("Partial JustFloat frame was parsed.");
capture.AppendJustFloat(binary.AsSpan(5), first);
if (capture.SampleCount != 1 || capture.ChannelCount != 2 ||
    capture.GetPoints(0).Single().Value != 1.25 || capture.GetPoints(1).Single().Value != -2.5)
    throw new Exception("JustFloat frame parse failed.");
Console.WriteLine("Serial waveform capture smoke passed.");
