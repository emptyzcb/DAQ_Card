using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class CanTraceRecorder
{
    private readonly Channel<string> queue;
    private readonly Task worker;
    private Exception? failure;
    private bool completed;
    public CanTraceRecorder(string path, int capacity = 32768)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 16384, useAsync: true);
        queue = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        worker = Task.Run(() => WriteAsync(stream));
    }
    public Exception? Failure => Volatile.Read(ref failure);
    public bool Append(DateTime host, string direction, CanFrame frame)
    {
        if (Failure is not null || completed) return false;
        var row = string.Join(",", new[] { host.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture), direction,
            frame.Id.ToString(frame.Extended ? "X8" : "X3", CultureInfo.InvariantCulture),
            (frame.DriverFlags & 0x80) != 0 ? "STATUS" : (frame.DriverFlags & 0x40) != 0 ? "ERROR" :
                (frame.Extended ? "EXT" : "STD") + (frame.Remote ? "_RTR" : "_DATA"),
            frame.Dlc.ToString(CultureInfo.InvariantCulture), Convert.ToHexString(frame.Data),
            frame.DeviceTimestampUs?.ToString(CultureInfo.InvariantCulture) ?? "", frame.DriverStatus.ToString("X", CultureInfo.InvariantCulture) });
        if (queue.Writer.TryWrite(row)) return true;
        Interlocked.CompareExchange(ref failure, new IOException("CAN 记录缓冲区已满或写入失败，本帧及后续数据未记录。"), null);
        queue.Writer.TryComplete();
        return false;
    }
    private async Task WriteAsync(FileStream stream)
    {
        try
        {
            await using var output = new StreamWriter(stream, new UTF8Encoding(true));
            await output.WriteLineAsync("HostTime,Direction,ID_Hex,Type,DLC,Data_Hex,DeviceElapsedUs,DriverStatusHex");
            await foreach (var line in queue.Reader.ReadAllAsync()) await output.WriteLineAsync(line);
            await output.FlushAsync();
        }
        catch (Exception ex) { Interlocked.CompareExchange(ref failure, ex, null); queue.Writer.TryComplete(ex); }
    }
    public async Task FinishAsync()
    {
        if (!completed) { completed = true; queue.Writer.TryComplete(); }
        await worker.ConfigureAwait(false);
        if (Failure is { } error) throw new IOException("CAN 记录未完整保存：" + error.Message, error);
    }
}
