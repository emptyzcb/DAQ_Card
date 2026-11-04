namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class AcquisitionSnapshot
{
    public DateTime Timestamp { get; init; }
    public long SegmentId { get; init; }
    public double? ActualPollIntervalMs { get; init; }
    public DateTime? PollStartedAt { get; init; }
    public DateTime? DigitalInputsObservedAt { get; init; }
    public DateTime? Ad7606ObservedAt { get; init; }
    public DateTime? AnalogOutputsObservedAt { get; init; }
    public DateTime? Rs485ObservedAt { get; init; }
    public ushort Status { get; init; }
    public ushort DigitalInputs { get; init; }
    public ushort DigitalOutputs { get; init; }
    public ushort IoControllerState { get; init; }
    public ushort IoControllerFlags { get; init; }
    public ushort AnalogVoltageMv { get; init; }
    public ushort AnalogCurrentUa { get; init; }
    public short[] Ad7606Raw { get; init; } = new short[8];
    public ushort[]? LinkStats { get; init; }
    public IReadOnlyList<Rs485SensorTelemetry>? Rs485Sensors { get; init; }
}
