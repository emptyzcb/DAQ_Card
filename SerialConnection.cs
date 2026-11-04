using System.IO.Ports;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed record SerialConnectionOptions(
    string PortName, int BaudRate, int DataBits, Parity Parity, StopBits StopBits,
    Handshake Handshake = Handshake.None);

internal sealed class SerialConnection : IDisposable
{
    private readonly object writeLock = new();
    private SerialPort? port;

    public event Action<byte[]>? Received;
    public event Action<Exception>? Error;

    public bool IsOpen => port?.IsOpen == true;
    public string PortName => port?.PortName ?? string.Empty;

    public void Open(SerialConnectionOptions options)
    {
        if (IsOpen) throw new InvalidOperationException("串口已经打开。");
        var candidate = new SerialPort(options.PortName, options.BaudRate, options.Parity,
            options.DataBits, options.StopBits)
        {
            ReadTimeout = 100,
            WriteTimeout = 150,
            Handshake = options.Handshake
        };
        candidate.DataReceived += OnDataReceived;
        candidate.ErrorReceived += OnErrorReceived;
        try
        {
            candidate.Open();
            port = candidate;
        }
        catch
        {
            candidate.DataReceived -= OnDataReceived;
            candidate.ErrorReceived -= OnErrorReceived;
            candidate.Dispose();
            throw;
        }
    }

    public void Send(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        var data = bytes.ToArray();
        lock (writeLock)
        {
            var current = port;
            if (current?.IsOpen != true) throw new InvalidOperationException("请先打开串口。");
            current.Write(data, 0, data.Length);
        }
    }

    public void Close()
    {
        lock (writeLock)
        {
            var current = port;
            port = null;
            if (current is null) return;
            current.DataReceived -= OnDataReceived;
            current.ErrorReceived -= OnErrorReceived;
            current.Close();
            current.Dispose();
        }
    }

    public void SetDtr(bool enabled)
    {
        lock (writeLock)
        {
            if (port?.IsOpen != true) throw new InvalidOperationException("请先打开串口。");
            port.DtrEnable = enabled;
        }
    }

    public void SetRts(bool enabled)
    {
        lock (writeLock)
        {
            if (port?.IsOpen != true) throw new InvalidOperationException("请先打开串口。");
            port.RtsEnable = enabled;
        }
    }

    public void SendBreak(int milliseconds = 100)
    {
        lock (writeLock)
        {
            if (port?.IsOpen != true) throw new InvalidOperationException("请先打开串口。");
            port.BreakState = true;
            try { Thread.Sleep(milliseconds); }
            finally { port.BreakState = false; }
        }
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            if (sender is not SerialPort current || !current.IsOpen) return;
            var count = current.BytesToRead;
            if (count <= 0) return;
            var bytes = new byte[Math.Min(count, 4096)];
            var read = current.Read(bytes, 0, bytes.Length);
            if (read == 0) return;
            if (read != bytes.Length) Array.Resize(ref bytes, read);
            Received?.Invoke(bytes);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            Error?.Invoke(ex);
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e) =>
        Error?.Invoke(new IOException($"串口硬件错误：{e.EventType}"));

    public void Dispose() => Close();
}
