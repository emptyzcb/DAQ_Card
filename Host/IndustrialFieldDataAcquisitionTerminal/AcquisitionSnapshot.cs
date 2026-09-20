namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class AcquisitionSnapshot
{
    public DateTime Timestamp { get; init; }
    public ushort Status { get; init; }
    public ushort DigitalInputs { get; init; }
    public ushort DigitalOutputs { get; init; }
    public short[] Ad7606Raw { get; init; } = new short[8];
    public short[] ImuRaw { get; init; } = new short[6];
    public short RollCentiDegrees { get; init; }
    public short PitchCentiDegrees { get; init; }
    public short YawCentiDegrees { get; init; }
}
