using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CanSmoke")]

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed record CanFrame(uint Id, bool Extended, bool Remote, byte Dlc, byte[] Data, byte DriverFlags = 0, ulong? DeviceTimestampUs = null, uint DriverStatus = 0)
{
    public void ValidateForTransmit()
    {
        if (Id > (Extended ? 0x1FFFFFFFu : 0x7FFu)) throw new ArgumentException(Extended ? "扩展帧 ID 不能超过 1FFFFFFF。" : "标准帧 ID 不能超过 7FF。");
        if (Dlc > 8 || Data is null || (Remote ? Data.Length != 0 : Data.Length != Dlc))
            throw new ArgumentException("Classic CAN 数据长度为 0–8 字节；远程帧无数据，DLC 为请求长度。");
    }
}
internal sealed record CanChannel(ushort Handle, string Name, uint Condition);
internal sealed record CanBitrate(string Name, ushort Code) { public override string ToString() => Name; }

internal static class CanInterpreter
{
    public static byte[] ParseHex(string text)
    {
        var clean = text.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").Replace(",", "");
        if (clean.Length % 2 != 0 || clean.Length > 16 || clean.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("数据请填写 0–8 字节十六进制，例如 01 02 A0 FF。");
        return Convert.FromHexString(clean);
    }
    public static uint ParseId(string text, bool extended)
    {
        var value = text.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        if (!uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var id) || id > (extended ? 0x1FFFFFFFu : 0x7FFu))
            throw new ArgumentException(extended ? "请输入 0–1FFFFFFF 的扩展帧十六进制 ID。" : "请输入 0–7FF 的标准帧十六进制 ID。");
        return id;
    }
    public static string Explain(CanFrame frame)
    {
        if ((frame.DriverFlags & 0x80) != 0) return $"PCAN 状态消息 · 返回状态 0x{frame.DriverStatus:X} · 数据 {Convert.ToHexString(frame.Data)}；不是普通 CAN 数据帧。";
        if ((frame.DriverFlags & 0x40) != 0) return "CAN 错误帧；不能按普通数据帧解码。";
        if (frame.Remote) return $"{(frame.Extended ? "29 位扩展" : "11 位标准")}远程帧，请求 DLC={frame.Dlc}，不携带数据。";
        var data = frame.Data;
        var lines = new List<string> { $"{(frame.Extended ? "29 位扩展" : "11 位标准")}数据帧 · ID 0x{frame.Id:X} · {data.Length} 字节",
            "HEX: " + (data.Length == 0 ? "（空）" : Convert.ToHexString(data)),
            "逐字节无符号: " + (data.Length == 0 ? "（空）" : string.Join("  ", data.Select((b, i) => $"B{i}={b}"))) };
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            var le = data[i] | data[i + 1] << 8;
            var be = data[i] << 8 | data[i + 1];
            lines.Add($"B{i}–B{i + 1}: 小端 U16={le}, I16={unchecked((short)le)}；大端 U16={be}, I16={unchecked((short)be)}");
        }
        for (var i = 0; i + 3 < data.Length; i += 4)
        {
            var le = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4));
            var be = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i, 4));
            var fle = BitConverter.Int32BitsToSingle(unchecked((int)le));
            var fbe = BitConverter.Int32BitsToSingle(unchecked((int)be));
            lines.Add($"B{i}–B{i + 3}: 小端 U32={le}, I32={unchecked((int)le)}, F32={fle:G6}；大端 U32={be}, I32={unchecked((int)be)}, F32={fbe:G6}");
        }
        lines.Add("ASCII: " + new string(data.Select(b => b is >= 32 and <= 126 ? (char)b : '.').ToArray()));
        lines.Add("以上是原始字节的数值解释；实际物理含义、字节序和缩放须以设备协议为准。");
        return string.Join(Environment.NewLine, lines);
    }
}

internal interface ICanAdapter : IDisposable
{
    IReadOnlyList<CanChannel> ListChannels();
    void Open(ushort handle, ushort bitrate, bool listenOnly = false);
    CanFrame? Read();
    void Write(CanFrame frame);
    string BusStatus();
    uint BusStatusCode();
    bool IsOpen { get; }
    bool IsListenOnly { get; }
    void Close();
}

// Native signatures and values are from PEAK-System's PCANBasic.h distributed in PCAN-Basic 5.1.
internal sealed class PcanAdapter : ICanAdapter
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public uint Id; public byte Flags; public byte Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Data;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeTimestamp { public uint Millis; public ushort Overflow; public ushort Micros; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Initialize(ushort handle, ushort bitrate, byte hw, uint io, ushort interrupt);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint Uninitialize(ushort handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReadMessage(ushort handle, out NativeMessage message, out NativeTimestamp timestamp);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint WriteMessage(ushort handle, ref NativeMessage message);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint GetValue(ushort handle, byte parameter, out uint value, uint length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint SetValue(ushort handle, byte parameter, ref uint value, uint length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint GetStatus(ushort handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint GetErrorText(uint code, ushort language, StringBuilder text);
    private readonly string? libraryPath;
    public PcanAdapter(string? libraryPath = null) => this.libraryPath = libraryPath;
    private IntPtr library;
    private Initialize? initialize; private Uninitialize? uninitialize; private ReadMessage? read;
    private WriteMessage? write; private GetValue? getValue; private SetValue? setValue; private GetStatus? getStatus; private GetErrorText? getErrorText;
    private ushort channel;
    public bool IsOpen { get; private set; }
    public bool IsListenOnly { get; private set; }
    private void EnsureLibrary()
    {
        if (library != IntPtr.Zero) return;
        var arch = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "x64", Architecture.X86 => "x86", Architecture.Arm64 => "ARM64", _ => throw new PlatformNotSupportedException("此 CPU 架构未提供 PCAN 驱动接口。") };
        var path = libraryPath ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Pcan", arch, "PCANBasic.dll");
        try
        {
            library = NativeLibrary.Load(path);
            T Load<T>(string symbol) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, symbol));
            initialize = Load<Initialize>("CAN_Initialize"); uninitialize = Load<Uninitialize>("CAN_Uninitialize");
            read = Load<ReadMessage>("CAN_Read"); write = Load<WriteMessage>("CAN_Write");
            getValue = Load<GetValue>("CAN_GetValue"); setValue = Load<SetValue>("CAN_SetValue"); getStatus = Load<GetStatus>("CAN_GetStatus");
            getErrorText = Load<GetErrorText>("CAN_GetErrorText");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; }
            throw new InvalidOperationException($"无法加载 PCAN-Basic 接口，请检查 {path} 和 PEAK 驱动。{ex.Message}", ex);
        }
    }
    private string ExplainError(uint code)
    {
        var text = new StringBuilder(256);
        return getErrorText is not null && getErrorText(code, 9, text) == 0 ? $"{text} (0x{code:X})" : $"PCAN 错误 0x{code:X}";
    }
    public IReadOnlyList<CanChannel> ListChannels()
    {
        EnsureLibrary();
        var channels = new List<CanChannel>();
        for (int index = 1; index <= 16; index++)
        {
            var handle = (ushort)(index <= 8 ? 0x50 + index : 0x500 + index);
            var result = getValue!(handle, 0x0D, out var condition, 4);
            if ((result & 0x200u) != 0) throw new InvalidOperationException("PCAN 设备驱动未加载：" + ExplainError(result));
            if (result == 0 && condition != 0) channels.Add(new(handle, $"PCAN-USB {index} (0x{handle:X})", condition));
        }
        return channels;
    }
    public void Open(ushort handle, ushort bitrate, bool listenOnly = false)
    {
        if (IsOpen) throw new InvalidOperationException("请先断开当前 CAN 通道。");
        EnsureLibrary();
        if (listenOnly)
        {
            uint enabled = 1;
            var setResult = setValue!(handle, 0x08, ref enabled, sizeof(uint));
            if (setResult != 0) throw new InvalidOperationException("无法启用硬件只监听模式：" + ExplainError(setResult));
        }
        var result = initialize!(handle, bitrate, 0, 0, 0);
        if (result == 0x02000000u)
        {
            // PCAN initialized the channel at a different bit rate. Release it; do not silently misrepresent the speed.
            var closeResult = uninitialize!(handle);
            throw new InvalidOperationException("通道已被其他程序以不同波特率占用，请关闭该程序或统一波特率。" +
                (closeResult == 0 ? "" : " 释放通道失败：" + ExplainError(closeResult)));
        }
        if (result != 0)
        {
            if (listenOnly) { uint off = 0; setValue!(handle, 0x08, ref off, sizeof(uint)); }
            throw new InvalidOperationException(ExplainError(result));
        }
        var check = getValue!(handle, 0x08, out var actualMode, sizeof(uint));
        if (check != 0 || (actualMode != 0) != listenOnly)
        {
            var closeResult = uninitialize!(handle);
            throw new InvalidOperationException("无法确认所选 CAN 监听模式，连接已取消。" + (check != 0 ? ExplainError(check) : "") +
                (closeResult == 0 ? "" : " 释放失败：" + ExplainError(closeResult)));
        }
        channel = handle; IsOpen = true; IsListenOnly = listenOnly;
    }
    public CanFrame? Read()
    {
        if (!IsOpen) throw new InvalidOperationException("CAN 尚未连接。");
        var result = read!(channel, out var native, out var stamp);
        if (result == 0x20u) return null;
        if (result != 0 && (native.Flags & 0x80) == 0) throw new InvalidOperationException(ExplainError(result));
        if (native.Length > 8 || native.Data is null || native.Data.Length < 8) throw new InvalidDataException("设备返回无效 Classic CAN 数据长度。");
        var remote = (native.Flags & 1) != 0;
        var payload = remote ? Array.Empty<byte>() : native.Data.Take(native.Length).ToArray();
        return new(native.Id, (native.Flags & 2) != 0, remote, native.Length, payload, native.Flags,
            ((ulong)stamp.Overflow * 0x100000000UL + stamp.Millis) * 1000UL + stamp.Micros, result);
    }
    public void Write(CanFrame frame)
    {
        if (!IsOpen) throw new InvalidOperationException("CAN 尚未连接。");
        if (IsListenOnly) throw new InvalidOperationException("当前为只监听模式，不能发送报文。");
        frame.ValidateForTransmit();
        var bytes = new byte[8]; frame.Data.CopyTo(bytes, 0);
        var native = new NativeMessage { Id = frame.Id, Flags = (byte)((frame.Extended ? 2 : 0) | (frame.Remote ? 1 : 0)), Length = frame.Dlc, Data = bytes };
        var result = write!(channel, ref native);
        if (result != 0) throw new InvalidOperationException(ExplainError(result));
    }
    public uint BusStatusCode() => IsOpen ? getStatus!(channel) : 0x04000000u;
    public string BusStatus()
    {
        if (!IsOpen) return "未连接";
        var status = BusStatusCode();
        return status == 0 ? "OK" : ExplainError(status);
    }
    public void Close()
    {
        if (!IsOpen) return;
        var result = uninitialize!(channel);
        IsOpen = false; IsListenOnly = false;
        if (result != 0) throw new InvalidOperationException(ExplainError(result));
    }
    public void Dispose()
    {
        try { Close(); }
        finally { if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; } }
    }
}
