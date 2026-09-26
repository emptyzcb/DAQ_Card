using System.Diagnostics;
using System.IO.Ports;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class ModbusException : Exception
{
    public ModbusException(string message) : base(message)
    {
    }
}

internal sealed class ModbusRtuClient : IDisposable
{
    private readonly object sync = new();
    private SerialPort? serialPort;

    public byte SlaveAddress { get; private set; } = 1;
    public bool IsOpen => serialPort?.IsOpen == true;
    public int ReadTimeoutMs { get; set; } = 700;

    public event Action<string>? LogMessage;

    public void Open(string portName, int baudRate, byte slaveAddress)
    {
        Close();

        if (string.IsNullOrWhiteSpace(portName))
        {
            throw new ArgumentException("请选择串口。", nameof(portName));
        }

        if (slaveAddress is 0 or > 247)
        {
            throw new ArgumentOutOfRangeException(nameof(slaveAddress), "Modbus 从机地址必须在 1~247，广播地址不用于轮询。");
        }

        var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = ReadTimeoutMs,
            WriteTimeout = ReadTimeoutMs,
            DtrEnable = false,
            RtsEnable = false,
            Handshake = Handshake.None,
            ReadBufferSize = 4096,
            WriteBufferSize = 4096
        };

        port.Open();
        serialPort = port;
        SlaveAddress = slaveAddress;
        Log($"串口已打开：{portName}, {baudRate} 8N1, 从机地址={slaveAddress}");
    }

    public void Close()
    {
        lock (sync)
        {
            if (serialPort is null)
            {
                return;
            }

            try
            {
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                }
            }
            finally
            {
                serialPort.Dispose();
                serialPort = null;
            }
        }
    }

    public ushort[] ReadHoldingRegisters(ushort startAddress, ushort quantity)
    {
        if (quantity is 0 or > 125)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        var request = new byte[8];
        request[0] = SlaveAddress;
        request[1] = 0x03;
        WriteUInt16(request, 2, startAddress);
        WriteUInt16(request, 4, quantity);
        AppendCrc(request);

        var response = Transact(request, 0x03);
        var byteCount = response[2];
        if (byteCount != quantity * 2 || response.Length != byteCount + 5)
        {
            throw new ModbusException($"读寄存器响应长度错误：byteCount={byteCount}。");
        }

        var values = new ushort[quantity];
        for (var index = 0; index < quantity; index++)
        {
            values[index] = ReadUInt16(response, 3 + index * 2);
        }

        return values;
    }

    public void WriteSingleRegister(ushort address, ushort value)
    {
        var request = new byte[8];
        request[0] = SlaveAddress;
        request[1] = 0x06;
        WriteUInt16(request, 2, address);
        WriteUInt16(request, 4, value);
        AppendCrc(request);

        var response = Transact(request, 0x06);
        if (response.Length != request.Length || !response.AsSpan(0, 6).SequenceEqual(request.AsSpan(0, 6)))
        {
            throw new ModbusException("写单寄存器响应内容不匹配。");
        }
    }

    public byte[] TransactCustom(byte function,
                                 byte command,
                                 ReadOnlySpan<byte> payload,
                                 int expectedResponseLength,
                                 int? responseTimeoutMs = null)
    {
        if (expectedResponseLength < 5 || expectedResponseLength > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedResponseLength));
        }

        lock (sync)
        {
            var port = serialPort;
            if (port is null || !port.IsOpen)
            {
                throw new InvalidOperationException("串口尚未打开。");
            }

            var request = new byte[3 + payload.Length + 2];
            request[0] = SlaveAddress;
            request[1] = function;
            request[2] = command;
            payload.CopyTo(request.AsSpan(3));
            AppendCrc(request);
            port.ReadTimeout = responseTimeoutMs ?? ReadTimeoutMs;
            port.WriteTimeout = ReadTimeoutMs;
            port.DiscardInBuffer();
            port.Write(request, 0, request.Length);
            Log($"TX  {ToHex(request)}");

            var header = ReadExact(port, 3);
            if (header[0] != SlaveAddress)
            {
                throw new ModbusException($"IOCF 响应地址错误：0x{header[0]:X2}。");
            }

            if (header[1] != (byte)(function | 0x80))
            {
                // IOCF 错误响应是固定的 [addr, func, cmd, status, crc16]；
                // 成功响应才按照命令对应的固定长度继续读取。
                var status = ReadExact(port, 1)[0];
                var responseLength = status == 0 ? expectedResponseLength : 6;
                var response = new byte[responseLength];
                header.CopyTo(response, 0);
                response[3] = status;
                var remaining = ReadExact(port, responseLength - 4);
                remaining.CopyTo(response, 4);
                Log($"RX  {ToHex(response)}");

                if (!CheckCrc(response))
                {
                    throw new ModbusException("IOCF 响应 CRC 校验失败。");
                }

                if (response[3] != 0)
                {
                    throw new ModbusException($"MCU 返回 IOCF 错误：命令=0x{response[2]:X2}，错误码=0x{response[3]:X2}。");
                }

                if (response[1] != function)
                {
                    throw new ModbusException($"IOCF 响应功能码错误：0x{response[1]:X2}。");
                }

                return response;
            }

            var exceptionResponseLength = 5;
            var exceptionResponse = new byte[exceptionResponseLength];
            header.CopyTo(exceptionResponse, 0);
            var exceptionRemaining = ReadExact(port, exceptionResponseLength - header.Length);
            exceptionRemaining.CopyTo(exceptionResponse, header.Length);
            Log($"RX  {ToHex(exceptionResponse)}");

            if (!CheckCrc(exceptionResponse))
            {
                throw new ModbusException("IOCF 响应 CRC 校验失败。");
            }

            if (exceptionResponse[1] == (byte)(function | 0x80))
            {
                throw new ModbusException($"MCU 返回 IOCF 异常：错误码=0x{exceptionResponse[2]:X2}。");
            }

            if (exceptionResponse[1] != function)
            {
                throw new ModbusException($"IOCF 响应功能码错误：0x{exceptionResponse[1]:X2}。");
            }

            return exceptionResponse;
        }
    }

    private byte[] Transact(byte[] request, byte function)
    {
        lock (sync)
        {
            var port = serialPort;
            if (port is null || !port.IsOpen)
            {
                throw new InvalidOperationException("串口尚未打开。");
            }

            port.ReadTimeout = ReadTimeoutMs;
            port.WriteTimeout = ReadTimeoutMs;
            port.DiscardInBuffer();
            port.Write(request, 0, request.Length);
            Log($"TX  {ToHex(request)}");

            var header = ReadExact(port, 3);
            var expectedFunction = function;
            var responseLength = 0;

            if (header[0] != SlaveAddress)
            {
                throw new ModbusException($"响应地址错误：0x{header[0]:X2}。");
            }

            if (header[1] == (byte)(function | 0x80))
            {
                responseLength = 5;
            }
            else
            {
                if (header[1] != expectedFunction)
                {
                    throw new ModbusException($"响应功能码错误：0x{header[1]:X2}。");
                }

                responseLength = header[2] + 5;
                if (responseLength > 256)
                {
                    throw new ModbusException("响应帧超过 Modbus RTU 最大长度。");
                }
            }

            var response = new byte[responseLength];
            header.CopyTo(response, 0);
            var remaining = ReadExact(port, responseLength - header.Length);
            remaining.CopyTo(response, header.Length);
            Log($"RX  {ToHex(response)}");
            ValidateResponse(response, function);
            return response;
        }
    }

    private void ValidateResponse(byte[] response, byte function)
    {
        if (!CheckCrc(response))
        {
            throw new ModbusException("响应 CRC 校验失败。");
        }

        if (response[1] == (byte)(function | 0x80))
        {
            throw new ModbusException($"设备返回 Modbus 异常：功能码=0x{function:X2}, 异常码=0x{response[2]:X2}。");
        }
    }

    private static byte[] ReadExact(SerialPort port, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        var stopwatch = Stopwatch.StartNew();

        while (offset < count)
        {
            try
            {
                offset += port.Read(buffer, offset, count - offset);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException($"等待 Modbus 响应超时，已接收 {offset}/{count} 字节。", ex);
            }

            if (stopwatch.ElapsedMilliseconds > port.ReadTimeout + 1000)
            {
                throw new TimeoutException($"等待 Modbus 响应超时，已接收 {offset}/{count} 字节。");
            }
        }

        return buffer;
    }

    private void Log(string message)
    {
        LogMessage?.Invoke($"{DateTime.Now:HH:mm:ss.fff}  {message}");
    }

    public static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    private static void AppendCrc(byte[] frame)
    {
        var crc = Crc16(frame.AsSpan(0, frame.Length - 2));
        frame[^2] = (byte)crc;
        frame[^1] = (byte)(crc >> 8);
    }

    private static bool CheckCrc(byte[] frame)
    {
        var expected = Crc16(frame.AsSpan(0, frame.Length - 2));
        var actual = (ushort)(frame[^2] | (frame[^1] << 8));
        return expected == actual;
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static ushort ReadUInt16(byte[] buffer, int offset)
    {
        return (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
    }

    private static string ToHex(ReadOnlySpan<byte> data)
    {
        return Convert.ToHexString(data).InsertEvery(2, ' ');
    }

    public void Dispose()
    {
        Close();
    }
}

internal static class StringExtensions
{
    public static string InsertEvery(this string value, int interval, char separator)
    {
        if (value.Length <= interval)
        {
            return value;
        }

        var result = new System.Text.StringBuilder(value.Length + value.Length / interval);
        for (var index = 0; index < value.Length; index += interval)
        {
            if (index > 0)
            {
                result.Append(separator);
            }

            result.Append(value, index, Math.Min(interval, value.Length - index));
        }

        return result.ToString();
    }
}
